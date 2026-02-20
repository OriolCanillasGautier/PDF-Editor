"""
pdf2docx-cli  –  thin command-line wrapper around pdf2docx 0.5.9

Usage (called by HybridDocxExportProvider):
    pdf2docx-cli convert <input.pdf> <output.docx> [--pages 1,2,3] [--password PWD]
    pdf2docx-cli version

Exit codes:
    0  success
    1  conversion error
    2  bad arguments
"""

import argparse
import sys


def cmd_convert(args: argparse.Namespace) -> int:
    try:
        from pdf2docx import Converter  # type: ignore
    except ImportError as exc:
        print(f"ERROR: pdf2docx not available: {exc}", file=sys.stderr)
        return 1

    pages = None
    if args.pages:
        try:
            # Input is comma-separated 1-based page numbers; Converter wants 0-based
            pages = [int(p.strip()) - 1 for p in args.pages.split(",") if p.strip()]
        except ValueError:
            print(f"ERROR: --pages must be comma-separated integers, got: {args.pages!r}", file=sys.stderr)
            return 2

    try:
        cv = Converter(args.pdf, password=args.password or "")
        cv.convert(args.docx, pages=pages, multi_processing=False)
        cv.close()
        print(f"OK: {args.docx}")
        return 0
    except Exception as exc:  # noqa: BLE001
        print(f"ERROR: {exc}", file=sys.stderr)
        return 1


def cmd_version(_args: argparse.Namespace) -> int:
    try:
        import pdf2docx  # type: ignore
        version = getattr(pdf2docx, "__version__", "0.5.9")
    except ImportError:
        version = "unavailable"
    print(f"pdf2docx-cli wrapper, pdf2docx {version}")
    return 0


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="pdf2docx-cli",
        description="Convert PDF files to DOCX using pdf2docx.",
    )
    sub = parser.add_subparsers(dest="command")

    # -- convert sub-command
    p_convert = sub.add_parser("convert", help="Convert a PDF to DOCX.")
    p_convert.add_argument("pdf", help="Path to the input PDF file.")
    p_convert.add_argument("docx", help="Path for the output DOCX file.")
    p_convert.add_argument(
        "--pages",
        default=None,
        metavar="1,2,3",
        help="Comma-separated 1-based page numbers to convert (default: all).",
    )
    p_convert.add_argument(
        "--password",
        default=None,
        help="Password for encrypted PDFs.",
    )

    # -- version sub-command
    sub.add_parser("version", help="Print version information.")

    return parser


def main() -> None:
    parser = build_parser()
    args = parser.parse_args()

    if args.command == "convert":
        sys.exit(cmd_convert(args))
    elif args.command == "version":
        sys.exit(cmd_version(args))
    else:
        parser.print_help()
        sys.exit(2)


if __name__ == "__main__":
    main()
