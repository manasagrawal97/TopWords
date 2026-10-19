# TopWords

Prints the 20 most frequent words in a text file, along with their counts. A
"word" is a maximal run of ASCII letters (A-Z, a-z); everything else is a
separator, and matching is case-insensitive. It is equivalent to this Bash
pipeline:

```bash
cat "$1" | tr -cs 'a-zA-Z' '[\n*]' | grep -v "^$" | tr '[:upper:]' '[:lower:]' | sort | uniq -c | sort -nr | head -20
```

## Requirements

- .NET 10 SDK
- 64-bit Windows is the reference platform (what this was built and tested
  on); the code itself has no Windows-specific dependencies.

## Build and run

```
dotnet build -c Release
dotnet run -c Release -- "path\to\file.txt"
```

Or run the built executable directly:

```
bin\Release\net10.0\TopWords.exe "path\to\file.txt"
```

Results go to stdout. Errors go to stderr, and the exit code tells you what
happened:

| Code | Meaning |
|------|---------|
| 0 | Success |
| 1 | Usage error (wrong number of arguments) |
| 2 | File error (path doesn't exist, or is a folder) |
| 3 | Unexpected error |

## Design decisions

- **Reads raw bytes, not decoded text.** A byte is a letter if it's in the
  ASCII ranges A-Z or a-z; nothing else is. This means the program never has
  to pick or guess an encoding, so it behaves the same way the Bash pipeline
  does on any input: binary data, UTF-16, or UTF-8 with accented characters
  all just pass through with non-ASCII bytes acting as separators.
- **Streams the file in 64 KB blocks.** Memory use depends on the number of
  distinct words found, not on the size of the input file, so a 1 GB file
  and a 1 KB file use roughly the same amount of memory (see Testing below).
- **`Dictionary<string, long>` for counting.** Straightforward O(1) average
  lookup/update per word, which is all this needs for a single pass.
- **ASCII-only letters.** A consequence of the byte-based approach above:
  accented or non-Latin letters are not treated as word characters, so a
  word like "café" is counted as "caf". This matches the Bash pipeline
  exactly, since `tr -cs 'a-zA-Z'` has the same limitation.
- **Ties are ordered like `sort -nr`.** When two words have the same count,
  `sort -nr` falls back to sorting the text reverse-alphabetically, so this
  program breaks ties the same way (ordinal, descending).
- **7-column count field, like `uniq -c`.** `{count,7} {word}` — 7 is a
  minimum width, not a fixed one, so counts with more than 7 digits (as in
  the huge-file test below) still print without truncation or an alignment
  bug.
- **A pre-check before the try/catch.** `TryValidateFile` exists only to
  give a friendlier message for the common cases (missing file, path is a
  folder). The file can still disappear, get locked, or otherwise fail
  between the check and the read, which is what the surrounding try/catch
  is actually for.
- **Single-threaded.** The work is I/O bound — reading and decoding bytes is
  cheap compared to waiting on disk/OS I/O — so multiple threads would add
  complexity without a meaningful speedup, and the measurements below back
  that up. If a much faster version were needed, I'd split the file into
  byte ranges, give each a worker with its own `Dictionary<string, long>`
  reading in parallel, and merge the dictionaries at the end (taking care to
  not split a word across a range boundary).

## Testing

I ran the real Bash pipeline (via Git Bash) side by side with this program
on the same inputs and diffed the output. On a 417,986-byte sample
(`mobydick.txt`), all 20 output lines matched exactly.

I also tested these edge cases, each against the Bash pipeline where
applicable:

| Case | Expected | Result |
|------|----------|--------|
| Empty file | No output, exit 0 | Pass |
| 1 MB of random binary bytes | Exit 0, no crash, matches Bash | Pass |
| Text with no trailing newline (`"hello world hello"`) | `hello 2`, `world 1` | Pass |
| UTF-16 file | Matches Bash (bytes split into individual letters) | Pass |
| UTF-8 with accented characters (`"café café"`) | Matches Bash (`caf` counted, accents dropped) | Pass |
| Mixed case (`"The THE the"`) | `the 3` | Pass |
| Tie-break order (`"b a c"`, all count 1) | `c`, `b`, `a` (reverse alphabetical) | Pass |
| Non-existent path | Message on stderr, exit 2 | Pass |
| Folder path | Message on stderr, exit 2 | Pass |
| No argument | Usage on stderr, exit 1 | Pass |
| Two arguments | Usage on stderr, exit 1 | Pass |
| File held open by another process (`FileShare.ReadWrite`) | Still readable | Pass |

### Huge-file test

I built a ~1 GB file by concatenating `mobydick.txt` 2,500 times, with a
single newline byte after each copy so words don't glue across copies
(1,044,967,500 bytes total). Every output count was exactly 2,500x the
count from the single file, e.g.:

```
10710000 the
 5480000 and
 5462500 of
```

Measured on the Release build, second (warm) run:

- Wall-clock time: ~11.1-11.2 seconds
- Peak working set: ~46-47 MB

The memory figure is the important one: it stays flat regardless of file
size because the program only ever holds the distinct words (a few thousand
of them, here) in memory, not the file itself.

To recreate the huge file:

```powershell
$src = "path\to\mobydick.txt"
$dest = "path\to\huge.txt"
$bytes = [System.IO.File]::ReadAllBytes($src)
$newline = [byte[]]@(10)
$fs = [System.IO.File]::Create($dest)
try {
    for ($i = 0; $i -lt 2500; $i++) {
        $fs.Write($bytes, 0, $bytes.Length)
        $fs.Write($newline, 0, 1)
    }
} finally {
    $fs.Close()
}
```

## Known limits / trade-offs

- One `string` is allocated per word occurrence before the dictionary
  lookup, so there's allocation/GC pressure proportional to the total word
  count, not just the distinct word count.
- Memory grows with the number of distinct words in the file, not the file
  size — fine for natural-language text, but a pathological file with huge
  numbers of distinct "words" could use significant memory.
- Only ASCII letters are treated as word characters; accented and
  non-Latin text is split up at every non-ASCII byte, same as the Bash
  pipeline this mirrors.
