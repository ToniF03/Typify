# TypstToPdfRenderer

`TypstToPdfRenderer` is a standalone console front end for compiling Typst source files to PDF. It invokes the official Typst CLI, so Typst language support and PDF generation come from the compiler instead of a partial reimplementation.

The Typst compiler must be installed and available on `PATH`. To use a specific executable, pass `--typst-path`.

```text
TypstToPdfRenderer [--typst-path <typst.exe>] <input.typ> [output.pdf] [Typst compile options]
```

Examples:

```text
TypstToPdfRenderer report.typ
TypstToPdfRenderer report.typ report.pdf --pdf-standard 2.0
TypstToPdfRenderer --typst-path "C:\Tools\typst.exe" report.typ report.pdf
```

Options not owned by the wrapper are forwarded to `typst compile`, including options for project roots, fonts, inputs, page ranges, and PDF standards. When no output path is supplied, Typst selects the PDF path from the input file name. See the [Typst PDF reference](https://typst.app/docs/reference/pdf/) and [CLI documentation](https://typst.app/docs/).
