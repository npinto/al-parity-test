#!/usr/bin/env python3
"""Analyze drcov binary coverage data to calculate target.dll coverage.

Usage:
    python3 analyze_drcov.py <drcov_log> [--dll <local_dll_path>]

The --dll argument is needed when analyzing drcov logs from CI (Windows paths)
on a local machine (macOS/Linux). Without it, objdump cannot read the DLL.
"""

import argparse
import struct
import subprocess
import sys
from pathlib import Path


# Expected export functions (our application code)
# These are the 28 documented exports from target.dll
EXPORT_FUNCTIONS = [
    "Aud_InitDll", "Aud_GetInterfaceVersion", "Aud_GetDllVersion",
    "Aud_OpenGetFile", "Aud_CloseGetFile", "Aud_GetNumberOfFiles",
    "Aud_GetNumberOfChannels", "Aud_FileExistsW",
    "Aud_OpenPutFile", "Aud_ClosePutFile", "Aud_PutNumberOfChannels", "Aud_MakeDirW",
    "Aud_GetChannelDataDoubles", "Aud_GetChannelDataOriginal",
    "Aud_PutChannelDataDoubles", "Aud_PutChannelDataOriginal",
    "Aud_GetFileProperties", "Aud_GetChannelProperties",
    "Aud_PutFileProperties", "Aud_PutChannelProperties",
    "Aud_GetFileHeaderOriginal", "Aud_PutFileHeaderOriginal",
    "Aud_GetString", "Aud_PutString",
    "Aud_TextFileAOpenW", "Aud_TextFileAClose", "Aud_ReadLineAInFile",
    "Aud_GetLastWarnings", "Aud_GetErrDescription"
]


def get_text_section_info(dll_path: str) -> tuple:
    """Get .text section size and start address from DLL using objdump.

    Returns (start_address, size) or (0, default_size) on failure.
    """
    try:
        result = subprocess.run(
            ["objdump", "-h", dll_path],
            capture_output=True,
            text=True,
            timeout=30
        )
        for line in result.stdout.split("\n"):
            if ".text" in line:
                # Format: idx name size vma lma fileoff align ...
                parts = line.split()
                if len(parts) >= 4:
                    size_hex = parts[2]
                    vma_hex = parts[3]  # Virtual memory address
                    return int(vma_hex, 16), int(size_hex, 16)
    except Exception as e:
        print(f"[WARN] Could not get .text info via objdump: {e}")

    # Fallback
    return 0, 168232


def get_export_addresses(dll_path: str) -> dict:
    """Get addresses of exported functions using objdump -p.

    Returns dict of function_name -> RVA offset.
    """
    exports = {}
    try:
        result = subprocess.run(
            ["objdump", "-p", dll_path],
            capture_output=True,
            text=True,
            timeout=30
        )
        lines = result.stdout.split("\n")
        in_export_table = False
        for line in lines:
            if "Export Table:" in line:
                in_export_table = True
                continue
            if in_export_table:
                # Skip header lines
                if "DLL name:" in line or "Ordinal base:" in line or "Ordinal" in line:
                    continue
                if line.strip() == "":
                    break
                # Format: "      ordinal   0xRVA  Name"
                # Example: "       1   0x35c0  Aud_CloseGetFile"
                parts = line.split()
                if len(parts) >= 3:
                    try:
                        # First part is ordinal (integer), second is RVA (hex), third is name
                        rva_str = parts[1]
                        if rva_str.startswith("0x"):
                            addr = int(rva_str, 16)
                            name = parts[2]
                            # Only use non-decorated names (without @N suffix)
                            if "@" not in name:
                                exports[name] = addr
                    except (ValueError, IndexError):
                        pass
    except Exception as e:
        print(f"[WARN] Could not get exports via objdump: {e}")

    return exports


def parse_drcov_file(filepath: str, local_dll_path: str = None):
    """Parse a drcov log file and extract coverage info for target.dll.

    Args:
        filepath: Path to drcov log file
        local_dll_path: Optional local path to target.dll for analysis.
                       Use this when the drcov log contains Windows paths
                       that can't be resolved locally.
    """

    with open(filepath, "rb") as f:
        content = f.read()

    # Find the header end (text portion)
    text_end = content.find(b"BB Table:")
    if text_end == -1:
        print("[FAIL] Could not find BB Table marker")
        return None

    # Parse text header
    text_part = content[:text_end].decode("utf-8", errors="replace")
    lines = text_part.strip().split("\n")

    # Parse module table
    modules = {}
    target_module = None
    module_count = 0

    for line in lines:
        if line.startswith("Module Table:"):
            # Extract count
            parts = line.split("count")
            if len(parts) > 1:
                module_count = int(parts[1].strip())
            continue

        # Parse module entry: id, containing_id, start, end, ...
        if line.strip().startswith("0,") or (line.strip() and line.strip()[0].isdigit()):
            parts = [p.strip() for p in line.split(",")]
            if len(parts) >= 10:
                try:
                    mod_id = int(parts[0])
                    start = int(parts[2], 16)
                    end = int(parts[3], 16)
                    path = parts[9].strip() if len(parts) > 9 else ""

                    size = end - start
                    modules[mod_id] = {
                        "id": mod_id,
                        "start": start,
                        "end": end,
                        "size": size,
                        "path": path
                    }

                    # Match both local (target.dll) and public (target.dll) names
                    if "target.dll" in path or "target.dll" in path:
                        target_module = modules[mod_id]
                        dll_name = "target.dll" if "target" in path else "target.dll"
                        print(f"[OK] Found {dll_name} as module {mod_id}")
                        print(f"     Address range: 0x{start:08x} - 0x{end:08x}")
                        print(f"     Size: {size:,} bytes ({size/1024:.1f} KB)")
                except (ValueError, IndexError):
                    pass

    if not target_module:
        print("[FAIL] Target DLL (target.dll or target.dll) not found in module table")
        return None

    # Parse BB Table header
    bb_header_start = text_end
    bb_header_end = content.find(b"\n", bb_header_start) + 1
    bb_header = content[bb_header_start:bb_header_end].decode("utf-8", errors="replace")

    # Extract BB count
    bb_count = 0
    if "BB Table:" in bb_header:
        parts = bb_header.split()
        for i, p in enumerate(parts):
            if p == "bbs" and i > 0:
                bb_count = int(parts[i-1])
                break

    print(f"\n[OK] Total basic blocks executed: {bb_count:,}")

    # Parse binary basic block data
    # Each entry is 8 bytes: (module_id:16, start_offset:32, size:16)
    binary_data = content[bb_header_end:]

    # Actually the format is different - let me check
    # drcov v3 format: each entry is 8 bytes total
    # - module_id: 2 bytes (uint16)
    # - start_offset: 4 bytes (uint32)
    # - size: 2 bytes (uint16)

    bb_entries = []
    target_bbs = []
    target_bytes_covered = set()

    offset = 0
    entry_size = 8

    while offset + entry_size <= len(binary_data) and len(bb_entries) < bb_count:
        # Parse entry
        try:
            start_offset = struct.unpack_from("<I", binary_data, offset)[0]
            size = struct.unpack_from("<H", binary_data, offset + 4)[0]
            module_id = struct.unpack_from("<H", binary_data, offset + 6)[0]

            bb_entries.append({
                "module_id": module_id,
                "offset": start_offset,
                "size": size
            })

            # Check if this is from target.dll
            if module_id == target_module["id"]:
                target_bbs.append({
                    "offset": start_offset,
                    "size": size
                })
                # Track covered bytes
                for i in range(size):
                    target_bytes_covered.add(start_offset + i)

            offset += entry_size
        except struct.error:
            break

    print(f"[OK] Parsed {len(bb_entries):,} basic block entries")
    print(f"\n=== target.dll Coverage ===")
    print(f"Basic blocks executed in target.dll: {len(target_bbs):,}")
    print(f"Bytes covered in target.dll: {len(target_bytes_covered):,}")

    # Calculate coverage percentage
    # Dynamically get .text section info from the actual DLL
    dll_size = target_module["size"]
    dll_path = local_dll_path if local_dll_path else target_module["path"]
    if local_dll_path:
        print(f"[OK] Using local DLL for analysis: {local_dll_path}")
    text_vma, code_section_size = get_text_section_info(dll_path)
    print(f"[OK] Detected .text section: VMA=0x{text_vma:x}, size={code_section_size:,} bytes")

    coverage_of_total = 100.0 * len(target_bytes_covered) / dll_size
    coverage_of_code = 100.0 * len(target_bytes_covered) / code_section_size

    print(f"\nDLL total size: {dll_size:,} bytes")
    print(f"Code section (.text): {code_section_size:,} bytes")
    print(f"Coverage (of total DLL): {coverage_of_total:.1f}%")
    print(f"Coverage (of code section): {coverage_of_code:.1f}%")

    # Export function coverage analysis
    print(f"\n=== Export Function Coverage ===")
    export_addrs = get_export_addresses(dll_path)
    exports_hit = 0
    exports_missed = []

    for func_name in EXPORT_FUNCTIONS:
        if func_name in export_addrs:
            func_addr = export_addrs[func_name]
            # Check if any basic block starts at or near this address
            func_hit = any(
                abs(bb["offset"] - func_addr) < 0x100  # Within 256 bytes
                for bb in target_bbs
            )
            if func_hit:
                exports_hit += 1
                print(f"  [OK] {func_name} (0x{func_addr:x})")
            else:
                exports_missed.append(func_name)
                print(f"  [  ] {func_name} (0x{func_addr:x}) - NOT HIT")
        else:
            exports_missed.append(func_name)
            print(f"  [??] {func_name} - NOT FOUND IN EXPORTS")

    export_coverage = 100.0 * exports_hit / len(EXPORT_FUNCTIONS)
    print(f"\nExport function coverage: {exports_hit}/{len(EXPORT_FUNCTIONS)} ({export_coverage:.1f}%)")

    # More detailed analysis
    if target_bbs:
        offsets = [bb["offset"] for bb in target_bbs]
        sizes = [bb["size"] for bb in target_bbs]
        print(f"\nOffset range: 0x{min(offsets):x} - 0x{max(offsets):x}")
        print(f"BB size range: {min(sizes)} - {max(sizes)} bytes")
        print(f"Average BB size: {sum(sizes)/len(sizes):.1f} bytes")

    # Calculate application code coverage (exports only, excluding CRT)
    if export_addrs:
        export_min = min(export_addrs.values())
        export_max = max(export_addrs.values()) + 0x200  # Add ~512 bytes for last function body
        app_code_size = export_max - export_min
        app_bytes_covered = set()
        for bb in target_bbs:
            for i in range(bb["size"]):
                addr = bb["offset"] + i
                if export_min <= addr < export_max:
                    app_bytes_covered.add(addr)
        app_coverage = 100.0 * len(app_bytes_covered) / app_code_size if app_code_size > 0 else 0

        print(f"\n=== Application Code Coverage (Exports Only) ===")
        print(f"Export address range: 0x{export_min:x} - 0x{export_max:x} ({app_code_size:,} bytes)")
        print(f"App bytes covered: {len(app_bytes_covered):,}")
        print(f"App code coverage: {app_coverage:.1f}%")

        # Find coverage gaps within export range
        all_export_addrs = set(range(export_min, export_max))
        uncovered = sorted(all_export_addrs - app_bytes_covered)
        if uncovered:
            # Group consecutive uncovered addresses into ranges
            gaps = []
            gap_start = uncovered[0]
            gap_end = uncovered[0]
            for addr in uncovered[1:]:
                if addr == gap_end + 1:
                    gap_end = addr
                else:
                    gaps.append((gap_start, gap_end, gap_end - gap_start + 1))
                    gap_start = addr
                    gap_end = addr
            gaps.append((gap_start, gap_end, gap_end - gap_start + 1))

            # Show largest gaps
            gaps.sort(key=lambda x: -x[2])
            print(f"\n=== Largest Coverage Gaps ===")
            print(f"Total gaps: {len(gaps)} (uncovered: {len(uncovered):,} bytes)")

            # Find which function each gap is in
            sorted_exports = sorted(export_addrs.items(), key=lambda x: x[1])
            for i, (start, end, size) in enumerate(gaps[:10]):
                # Find containing function
                containing_func = "unknown"
                for j, (fname, faddr) in enumerate(sorted_exports):
                    if faddr <= start:
                        if j + 1 < len(sorted_exports) and sorted_exports[j+1][1] > start:
                            containing_func = fname
                            break
                        elif j + 1 >= len(sorted_exports):
                            containing_func = fname
                            break
                print(f"  {i+1}. 0x{start:04x}-0x{end:04x} ({size:,} bytes) in {containing_func}")
    else:
        app_coverage = 0.0

    return {
        "module": target_module,
        "total_bbs": bb_count,
        "target_bbs": len(target_bbs),
        "bytes_covered": len(target_bytes_covered),
        "coverage_percent": coverage_of_code,
        "app_coverage_percent": app_coverage,
        "export_coverage": export_coverage,
        "exports_hit": exports_hit,
        "exports_total": len(EXPORT_FUNCTIONS)
    }


if __name__ == "__main__":
    parser = argparse.ArgumentParser(
        description="Analyze drcov binary coverage data for target.dll"
    )
    parser.add_argument(
        "log_path",
        nargs="?",
        default="drcov_artifacts/drcov_logs/drcov.coverage_test_driver.exe.06320.0000.proc.log",
        help="Path to drcov log file"
    )
    parser.add_argument(
        "--dll",
        dest="dll_path",
        default=None,
        help="Local path to target.dll (use when analyzing CI logs with Windows paths)"
    )
    args = parser.parse_args()

    print(f"Analyzing: {args.log_path}\n")
    result = parse_drcov_file(args.log_path, args.dll_path)

    if result:
        print("\n" + "=" * 60)
        print("BINARY COVERAGE SUMMARY")
        print("=" * 60)
        print(f"Export function coverage: {result['exports_hit']}/{result['exports_total']} ({result['export_coverage']:.1f}%)")
        print(f"Application code coverage: {result['app_coverage_percent']:.1f}%")
        print(f"Total .text coverage: {result['coverage_percent']:.1f}%")
        print(f"Basic blocks executed: {result['target_bbs']:,}")
        print(f"Bytes covered: {result['bytes_covered']:,}")
        print("")
        print("NOTE: 'Application code coverage' measures only exported functions,")
        print("excluding CRT code. This is the meaningful coverage metric.")
