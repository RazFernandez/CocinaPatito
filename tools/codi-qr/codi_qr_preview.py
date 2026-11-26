"""
CoDi QR preview tool.

Takes the JSON response of Pey's ``POST /codi/qr`` endpoint, explains its structure and renders
the ``qrString`` as a PNG image that any CoDi-enabled banking app can scan.

This is a standalone learning/testing utility. It is NOT part of the SplitBill backend: in
production the frontend renders the QR from the ``qrString`` returned by our API.

Example:
    python codi_qr_preview.py response.json
    python codi_qr_preview.py response.json --output my-qr.png --box-size 12
    type response.json | python codi_qr_preview.py -          (Windows, read from stdin)
"""

from __future__ import annotations

import argparse
import base64
import binascii
import json
import sys
from dataclasses import dataclass
from pathlib import Path

import qrcode
from qrcode.constants import ERROR_CORRECT_M

DEFAULT_OUTPUT_DIR = Path(__file__).resolve().parent / "output"


@dataclass(frozen=True)
class PeyQrResponse:
    """Minimal view of Pey's ``POST /codi/qr`` response.

    Attributes:
        idc: Charge identifier assigned by Banxico (Pey returns it as ``id``).
        qr_string: Exact text to encode in the QR image. Must be used verbatim.

    Example:
        >>> PeyQrResponse.parse('{"id": "35cd442ab9", "qrString": "{}"}').idc
        '35cd442ab9'
    """

    idc: str
    qr_string: str

    @staticmethod
    def parse(raw_json: str) -> "PeyQrResponse":
        """Parses the raw response body and validates the two fields we rely on.

        Raises:
            ValueError: The body is not JSON or ``id`` / ``qrString`` are missing or empty.
        """
        try:
            body = json.loads(raw_json)
        except json.JSONDecodeError as error:
            raise ValueError(f"Response is not valid JSON: {error}") from error

        idc = body.get("id") if isinstance(body, dict) else None
        qr_string = body.get("qrString") if isinstance(body, dict) else None
        if not isinstance(idc, str) or not idc:
            raise ValueError('Response has no "id" (the Banxico IDC).')
        if not isinstance(qr_string, str) or not qr_string:
            raise ValueError('Response has no "qrString" to encode.')
        return PeyQrResponse(idc=idc, qr_string=qr_string)


def describe_payload(response: PeyQrResponse) -> list[str]:
    """Builds a human-readable description of the CoDi payload inside ``qrString``.

    Long encrypted values are summarised by size, never printed in full.

    Example:
        >>> for line in describe_payload(response): print(line)
    """
    lines = [f"id (IDC)          : {response.idc}"]
    try:
        payload = json.loads(response.qr_string)
    except json.JSONDecodeError:
        return lines + ["qrString          : not JSON (encoded as-is)"]

    lines.append(f"TYP               : {payload.get('TYP')}  (CoDi message type)")

    device = (payload.get("v") or {}).get("DEV")
    if device:
        lines.append(f"v.DEV             : {device}  (collector registered at Banxico)")

    charge = payload.get("ic") or {}
    idc_inside = charge.get("IDC")
    match = "matches id" if idc_inside == response.idc else "DOES NOT match id"
    lines.append(f"ic.IDC            : {idc_inside}  ({match})")
    lines.append(f"ic.SER            : {charge.get('SER')}  (serial used to fetch the decryption key)")
    lines.append(f"ic.ENC            : {_size_of_base64(charge.get('ENC'))}  (amount, concept, account: encrypted)")
    lines.append(f"CRY               : {_size_of_base64(payload.get('CRY'))}  (integrity check)")
    return lines


def render_qr(qr_string: str, output: Path, box_size: int, border: int) -> qrcode.QRCode:
    """Encodes ``qr_string`` verbatim into a PNG and returns the QR object for reporting.

    Uses error-correction level M, the same settings as Pey's own tutorial.

    Example:
        >>> qr = render_qr(response.qr_string, Path("qr.png"), box_size=10, border=4)
        >>> qr.version  # QR size chosen automatically from the payload length
    """
    qr = qrcode.QRCode(version=None, error_correction=ERROR_CORRECT_M, box_size=box_size, border=border)
    qr.add_data(qr_string)  # never re-serialise: the bytes must be exactly what Pey returned
    qr.make(fit=True)
    output.parent.mkdir(parents=True, exist_ok=True)
    qr.make_image(fill_color="black", back_color="white").save(output)
    return qr


def _size_of_base64(value: object) -> str:
    if not isinstance(value, str):
        return "absent"
    try:
        return f"{len(base64.b64decode(value, validate=True))} bytes (base64, {len(value)} chars)"
    except (binascii.Error, ValueError):
        return f"{len(value)} chars (not base64)"


def _read_input(source: str) -> str:
    if source == "-":
        return sys.stdin.read()
    return Path(source).read_text(encoding="utf-8")


def main(argv: list[str] | None = None) -> int:
    """Command-line entry point. Returns the process exit code."""
    parser = argparse.ArgumentParser(description="Render a CoDi QR image from a Pey POST /codi/qr response.")
    parser.add_argument("response", help='Path to the JSON response, or "-" to read from stdin.')
    parser.add_argument("--output", type=Path, help="PNG path (default: output/<IDC>.png next to this script).")
    parser.add_argument("--box-size", type=int, default=10, help="Pixels per QR module (default 10).")
    parser.add_argument("--border", type=int, default=4, help="Quiet-zone width in modules (default 4, the QR minimum).")
    args = parser.parse_args(argv)

    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")

    try:
        response = PeyQrResponse.parse(_read_input(args.response))
    except (OSError, ValueError) as error:
        print(f"error: {error}", file=sys.stderr)
        return 1

    output = args.output or DEFAULT_OUTPUT_DIR / f"{response.idc}.png"
    qr = render_qr(response.qr_string, output, args.box_size, args.border)

    print("CoDi payload")
    print("------------")
    for line in describe_payload(response):
        print(line)
    print()
    modules = qr.modules_count
    pixels = (modules + 2 * args.border) * args.box_size
    print("QR image")
    print("--------")
    print(f"encoded bytes     : {len(response.qr_string.encode('utf-8'))}")
    print(f"QR version        : {qr.version}  ({modules} x {modules} modules)")
    print("error correction  : M (about 15% of the image can be damaged and still scan)")
    print(f"image size        : {pixels} x {pixels} px")
    print(f"saved to          : {output}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
