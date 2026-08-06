#!/usr/bin/env python3
"""앱 아이콘(.ico)을 부엉이 데이터에서 만든다.

    python3 win/make-icon.py

**손으로 그리지 않는다.** 그림의 원본은 `shared/owl.json` 이고 그건 맥 소스에서 나온다.
맥에서 부엉이를 고쳤으면 `dump_owl` 을 돌린 뒤 이걸 다시 돌린다.

PIL 없이 돈다 — 개발 기계에 파이썬 패키지를 깔게 만들지 않으려고 PNG 와 ICO 를
직접 쓴다. 둘 다 형식이 단순해서 그럴 값어치가 있다.
"""

import hashlib
import json
import pathlib
import struct
import zlib

ROOT = pathlib.Path(__file__).resolve().parent.parent
OWL = ROOT / "shared" / "owl.json"
OUT = ROOT / "win" / "src" / "DongCSU.App" / "Resources" / "DongCSU.ico"
# 아이콘이 지금 부엉이에서 나온 것인지 검사할 지문. 아래 fingerprint() 참고.
STAMP = OUT.with_suffix(".ico.sha256")

# 아이콘에 넣을 크기. 작업 표시줄·트레이·바로 가기가 저마다 다른 것을 고른다.
SIZES = [16, 24, 32, 48, 64, 128, 256]

MARK_TO_KEY = {"#": "body", "d": "wing", "l": "belly", "w": "face", "k": "pupil", "y": "beak"}


def fingerprint(grid, palette_hex, sizes):
    """아이콘을 만들어 낸 재료의 지문.

    아이콘 파일 자체를 비교하지 않는 이유: PNG 압축 결과가 zlib 판마다 달라서, 같은
    그림인데도 바이트가 달라진다. 그러면 CI 가 아무 이유 없이 빨개진다. 대신 **무엇으로
    만들었는지**를 비교한다 — 부엉이가 바뀌었는데 아이콘을 안 만든 경우만 잡으면 된다.

    C# 쪽(`--check-icon`)이 같은 문자열을 만든다. **한쪽을 고치면 다른 쪽도 고친다.**
    """
    parts = [
        "\n".join(grid),
        ",".join(f"{k}={palette_hex[k]}" for k in sorted(palette_hex)),
        ",".join(str(s) for s in sizes),
    ]
    return hashlib.sha256("|".join(parts).encode()).hexdigest()


def hex_to_rgb(value):
    value = value.lstrip("#")
    return tuple(int(value[i:i + 2], 16) for i in (0, 2, 4))


def png(width, height, pixels):
    """RGBA 픽셀(바이트열의 리스트, 한 줄씩)을 PNG 한 장으로."""
    raw = b"".join(b"\x00" + row for row in pixels)  # 줄마다 필터 0(없음)

    def chunk(tag, data):
        body = tag + data
        return struct.pack(">I", len(data)) + body + struct.pack(">I", zlib.crc32(body))

    return (
        b"\x89PNG\r\n\x1a\n"
        + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0))
        + chunk(b"IDAT", zlib.compress(raw, 9))
        + chunk(b"IEND", b"")
    )


def render(grid, palette, size):
    """부엉이를 size×size 안에 정수 배율로 그린다.

    한 칸이 정수가 아니면 어떤 행은 2px, 어떤 행은 3px가 되어 얼굴이 뭉개진다.
    그래서 내림한 뒤 남는 자리는 여백으로 둔다.
    """
    lines, columns = len(grid), len(grid[0])
    cell = max(1, min(size // columns, size // lines))
    art_w, art_h = cell * columns, cell * lines
    offset_x, offset_y = (size - art_w) // 2, (size - art_h) // 2

    rows = []
    for y in range(size):
        row = bytearray()
        grid_y = (y - offset_y) // cell if offset_y <= y < offset_y + art_h else -1
        for x in range(size):
            grid_x = (x - offset_x) // cell if offset_x <= x < offset_x + art_w else -1
            colour = None
            if grid_y >= 0 and grid_x >= 0:
                key = MARK_TO_KEY.get(grid[grid_y][grid_x])
                if key:
                    colour = palette[key]
            row += bytes(colour + (255,)) if colour else b"\x00\x00\x00\x00"
        rows.append(bytes(row))
    return png(size, size, rows)


def main():
    document = json.loads(OWL.read_text())
    palette_hex = document["palettes"]["normal"]
    palette = {k: hex_to_rgb(v) for k, v in palette_hex.items()}
    idle = next(a for a in document["animations"] if a["name"] == "idle")
    grid = idle["frames"][0]["grid"]

    images = [render(grid, palette, size) for size in SIZES]

    # ICO: 머리말 6바이트 + 항목마다 16바이트 + 이미지들. 256은 크기 칸에 0으로 적는다.
    offset = 6 + 16 * len(images)
    header = struct.pack("<HHH", 0, 1, len(images))
    entries = b""
    for size, data in zip(SIZES, images):
        entries += struct.pack(
            "<BBBBHHII", size % 256, size % 256, 0, 0, 1, 32, len(data), offset
        )
        offset += len(data)

    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_bytes(header + entries + b"".join(images))
    STAMP.write_text(fingerprint(grid, palette_hex, SIZES) + "\n")
    print(f"wrote: {OUT.relative_to(ROOT)}  ({OUT.stat().st_size:,} bytes, {len(images)} sizes)")
    print(f"wrote: {STAMP.relative_to(ROOT)}")


if __name__ == "__main__":
    main()
