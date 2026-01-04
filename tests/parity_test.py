#!/usr/bin/env python3
"""DLL Parity Test - Compare original vs rebuilt target.dll on Windows.

This script runs on GitHub Actions (Windows) to perform true parity testing
between the original DLL and our rebuilt C clone.

CRITICAL DISCOVERY (2026-01-02):
The original DLL uses a THREE-PHASE initialization protocol discovered by
reverse-engineering the .NET wrapper:

Phase 1: Call Aud_InitDll(0) - returns a challenge value
Phase 2: Calculate response = challenge XOR 1114983470, call Aud_InitDll(response)
Phase 3: Call Aud_InitDll(1230000000) - must return 1230000000 XOR 1826820242

Without completing all three phases, file I/O returns error -28.
"""

import ctypes
import json
import math
import os
import sys
from pathlib import Path
from datetime import datetime

# Paths
SCRIPT_DIR = Path(__file__).parent
ROOT_DIR = SCRIPT_DIR.parent
ORIGINAL_DLL = ROOT_DIR / "dlls" / "original" / "target.dll"
REBUILT_DLL = ROOT_DIR / "dlls" / "rebuilt" / "target.dll"
TEST_FILES_DIR = SCRIPT_DIR / "test_files"
RESULTS_DIR = SCRIPT_DIR / "results"

# Constants - from decompiled .NET code
AUD_MAGIC = 0x42754C2E

# The challenge-response constants from .NET decompilation
# num2 = 370 + pow(10, 2) = 470
# num2 %= 5000 = 470
# num2 += 1110000000 = 1110000470
# num2 |= 4 = 1110000470 (already has bit 2 set)
# num2 += sqrt(24830289000000) = 1110000470 + 4983000 = 1114983470
INIT_XOR_CONSTANT = 1114983470

# Phase 3 verification value: 0.23 * 1e9 + 1e9 = 1230000000
PHASE3_MAGIC = 1230000000
# Expected XOR result: 0x6CE32A92 = 1826820242
PHASE3_XOR_RESULT = 1826820242

# Format codes from decompiled wrapper
FORMAT_CODES = {
    '.etm': 1,   # FmtA
    '.efr': 2,   # FmtB
    '.emd': 3,   # FmtC
    '.etx': 5,   # FmtD
    '.wav': 9,   # MsWave
    '.tim': 10,  # MlssaTim
    '.frq': 11,  # MlssaFrq
    '.dat': 12,  # MonkeyForestDat
    '.spk': 13,  # MonkeyForestSpk
    '.frd': 24,  # ClioFreqText
    '.zma': 24,  # ClioFreqText (impedance)
}


def get_format_code(file_path: Path) -> int:
    """Get format code from file extension."""
    ext = file_path.suffix.lower()
    return FORMAT_CODES.get(ext, 0)  # 0 = auto-detect


class DLLWrapper:
    """Wrapper for target.dll functions."""

    def __init__(self, dll_path: Path, name: str):
        self.name = name
        self.dll_path = dll_path
        self.dll = None
        self._load()

    def _load(self):
        """Load the DLL."""
        if not self.dll_path.exists():
            raise FileNotFoundError(f"DLL not found: {self.dll_path}")

        # Change to DLL directory for dependencies
        old_cwd = os.getcwd()
        os.chdir(self.dll_path.parent)
        try:
            self.dll = ctypes.CDLL(str(self.dll_path))
            self._setup_functions()
        finally:
            os.chdir(old_cwd)

    def _setup_functions(self):
        """Setup function signatures."""
        # Aud_GetInterfaceVersion() -> double
        self.dll.Aud_GetInterfaceVersion.restype = ctypes.c_double
        self.dll.Aud_GetInterfaceVersion.argtypes = []

        # Aud_GetDllVersion() -> double
        self.dll.Aud_GetDllVersion.restype = ctypes.c_double
        self.dll.Aud_GetDllVersion.argtypes = []

        # Aud_InitDll(magic) -> uint
        self.dll.Aud_InitDll.restype = ctypes.c_uint
        self.dll.Aud_InitDll.argtypes = [ctypes.c_uint]

        # Aud_OpenGetFile(path, format_code, extra) -> int
        # CORRECTED: .NET signature is (string path, int format, int extra)
        self.dll.Aud_OpenGetFile.restype = ctypes.c_int
        self.dll.Aud_OpenGetFile.argtypes = [ctypes.c_wchar_p, ctypes.c_int, ctypes.c_int]

        # Aud_GetNumberOfFiles(out_count) -> int
        self.dll.Aud_GetNumberOfFiles.restype = ctypes.c_int
        self.dll.Aud_GetNumberOfFiles.argtypes = [ctypes.POINTER(ctypes.c_uint)]

        # Aud_GetNumberOfChannels(file_idx, out_count) -> int
        self.dll.Aud_GetNumberOfChannels.restype = ctypes.c_int
        self.dll.Aud_GetNumberOfChannels.argtypes = [ctypes.c_uint, ctypes.POINTER(ctypes.c_uint)]

        # Aud_CloseGetFile() -> int
        self.dll.Aud_CloseGetFile.restype = ctypes.c_int
        self.dll.Aud_CloseGetFile.argtypes = []

    def get_interface_version(self) -> float:
        return self.dll.Aud_GetInterfaceVersion()

    def get_dll_version(self) -> float:
        return self.dll.Aud_GetDllVersion()

    def init_dll_simple(self, magic: int = AUD_MAGIC) -> int:
        """Simple single-call init (for rebuilt DLL which doesn't need challenge-response)."""
        return self.dll.Aud_InitDll(magic)

    def init_dll_full(self) -> tuple[bool, str]:
        """Full three-phase initialization as used by the host application.

        This is required for the original DLL to enable file I/O operations.
        Discovered by reverse-engineering the host application wrapper.

        Returns:
            (success, message)
        """
        # Phase 1: Initial call with 0 to get challenge
        challenge = self.dll.Aud_InitDll(0)

        # Phase 2: Calculate response and verify
        response = challenge ^ INIT_XOR_CONSTANT
        result = self.dll.Aud_InitDll(response)
        if result != 0:
            return False, f"Phase 2 failed: Aud_InitDll({response}) returned {result}"

        # Phase 3: Final verification
        result = self.dll.Aud_InitDll(PHASE3_MAGIC)
        expected = PHASE3_MAGIC ^ PHASE3_XOR_RESULT
        if result != expected:
            return False, f"Phase 3 failed: expected {expected}, got {result}"

        return True, "Full three-phase initialization complete"

    def open_file(self, path: str, format_code: int = 0) -> int:
        """Open file with specified format code.

        Format codes (from decompilation):
            0 = Auto-detect (default)
            1 = FmtA (.etm)
            2 = FmtB (.efr)
            3 = FmtC (.emd)
            5 = FmtD (.etx)
            9 = MsWave (.wav)
            10 = MlssaTim (.tim)
            11 = MlssaFrq (.frq)
            12 = MonkeyForestDat (.dat)
            13 = MonkeyForestSpk (.spk)
            24 = ClioFreqText (.frd, .zma)
        """
        return self.dll.Aud_OpenGetFile(path, format_code, 0)

    def get_num_files(self) -> tuple[int, int]:
        count = ctypes.c_uint(0)
        ret = self.dll.Aud_GetNumberOfFiles(ctypes.byref(count))
        return ret, count.value

    def get_num_channels(self, file_idx: int = 0) -> tuple[int, int]:
        count = ctypes.c_uint(0)
        ret = self.dll.Aud_GetNumberOfChannels(file_idx, ctypes.byref(count))
        return ret, count.value

    def close_file(self) -> int:
        return self.dll.Aud_CloseGetFile()


def test_versions(original: DLLWrapper, rebuilt: DLLWrapper) -> dict:
    """Test version functions."""
    results = {"test": "versions", "passed": True, "details": []}

    # Interface version
    orig_iface = original.get_interface_version()
    rebuilt_iface = rebuilt.get_interface_version()
    match = orig_iface == rebuilt_iface
    results["details"].append({
        "function": "Aud_GetInterfaceVersion",
        "original": orig_iface,
        "rebuilt": rebuilt_iface,
        "match": match
    })
    if not match:
        results["passed"] = False

    # DLL version
    orig_ver = original.get_dll_version()
    rebuilt_ver = rebuilt.get_dll_version()
    match = orig_ver == rebuilt_ver
    results["details"].append({
        "function": "Aud_GetDllVersion",
        "original": orig_ver,
        "rebuilt": rebuilt_ver,
        "match": match
    })
    if not match:
        results["passed"] = False

    return results


def test_init(original: DLLWrapper, rebuilt: DLLWrapper) -> dict:
    """Test initialization with full three-phase protocol."""
    results = {"test": "init", "passed": True, "details": []}

    # Test rebuilt with simple init (our DLL doesn't need challenge-response)
    rebuilt_magic = rebuilt.init_dll_simple(AUD_MAGIC)
    rebuilt_ok = rebuilt_magic != 0

    results["details"].append({
        "function": "Aud_InitDll (simple)",
        "rebuilt_magic": hex(rebuilt_magic),
        "rebuilt_ok": rebuilt_ok
    })

    # Test original with full three-phase init
    orig_success, orig_msg = original.init_dll_full()
    results["details"].append({
        "function": "Aud_InitDll (full 3-phase)",
        "original_success": orig_success,
        "original_message": orig_msg
    })

    if not rebuilt_ok:
        results["passed"] = False
    if not orig_success:
        results["passed"] = False

    return results


def test_file_io_parity(original: DLLWrapper, rebuilt: DLLWrapper, test_file: Path) -> dict:
    """Test file I/O operations with PARITY comparison between original and rebuilt.

    This test requires that BOTH DLLs have been properly initialized:
    - Original: Full 3-phase initialization
    - Rebuilt: Simple init with AUD_MAGIC

    Returns detailed comparison of file I/O results.
    """
    results = {
        "test": f"file_io_parity_{test_file.name}",
        "passed": True,
        "details": []
    }

    file_path = str(test_file.resolve())
    format_code = get_format_code(test_file)

    # Open file with BOTH DLLs (using correct format code)
    orig_open = original.open_file(file_path, format_code)
    rebuilt_open = rebuilt.open_file(file_path, format_code)

    results["details"].append({
        "function": "Aud_OpenGetFile",
        "file": test_file.name,
        "format_code": format_code,
        "original": orig_open,
        "rebuilt": rebuilt_open,
        "match": orig_open == rebuilt_open
    })

    if orig_open != rebuilt_open:
        results["passed"] = False
        # If original still fails, note it
        if orig_open != 0:
            results["details"][-1]["note"] = f"Original DLL error {orig_open} - may need full host context"

    # If either failed to open, close and return
    if orig_open != 0 or rebuilt_open != 0:
        if orig_open == 0:
            original.close_file()
        if rebuilt_open == 0:
            rebuilt.close_file()
        return results

    # Get file count from both
    orig_ret, orig_files = original.get_num_files()
    rebuilt_ret, rebuilt_files = rebuilt.get_num_files()
    results["details"].append({
        "function": "Aud_GetNumberOfFiles",
        "original": {"ret": orig_ret, "count": orig_files},
        "rebuilt": {"ret": rebuilt_ret, "count": rebuilt_files},
        "match": orig_ret == rebuilt_ret and orig_files == rebuilt_files
    })

    if orig_ret != rebuilt_ret or orig_files != rebuilt_files:
        results["passed"] = False

    # Get channel count from both
    orig_ret, orig_channels = original.get_num_channels(0)
    rebuilt_ret, rebuilt_channels = rebuilt.get_num_channels(0)
    results["details"].append({
        "function": "Aud_GetNumberOfChannels",
        "original": {"ret": orig_ret, "count": orig_channels},
        "rebuilt": {"ret": rebuilt_ret, "count": rebuilt_channels},
        "match": orig_ret == rebuilt_ret and orig_channels == rebuilt_channels
    })

    if orig_ret != rebuilt_ret or orig_channels != rebuilt_channels:
        results["passed"] = False

    # Close both
    original.close_file()
    rebuilt.close_file()

    return results


def test_file_io_rebuilt_only(rebuilt: DLLWrapper, test_file: Path) -> dict:
    """Test file I/O operations on rebuilt DLL only (fallback when original fails).

    Used when original DLL initialization fails or returns errors.
    """
    results = {
        "test": f"file_io_rebuilt_{test_file.name}",
        "passed": True,
        "details": []
    }

    file_path = str(test_file.resolve())
    format_code = get_format_code(test_file)

    # Open file with rebuilt DLL (using correct format code)
    rebuilt_open = rebuilt.open_file(file_path, format_code)

    results["details"].append({
        "function": "Aud_OpenGetFile",
        "file": test_file.name,
        "format_code": format_code,
        "rebuilt": rebuilt_open,
        "expected": 0,
        "match": rebuilt_open == 0
    })

    if rebuilt_open != 0:
        results["passed"] = False
        rebuilt.close_file()
        return results

    # Check file count
    rebuilt_ret, rebuilt_files = rebuilt.get_num_files()
    results["details"].append({
        "function": "Aud_GetNumberOfFiles",
        "rebuilt": {"ret": rebuilt_ret, "count": rebuilt_files},
        "expected": {"ret": 0, "count": 1},
        "match": rebuilt_ret == 0 and rebuilt_files == 1
    })

    if rebuilt_ret != 0 or rebuilt_files != 1:
        results["passed"] = False

    # Get channels
    rebuilt_ret, rebuilt_channels = rebuilt.get_num_channels(0)
    results["details"].append({
        "function": "Aud_GetNumberOfChannels",
        "rebuilt": {"ret": rebuilt_ret, "count": rebuilt_channels},
        "expected": {"ret": 0, "count_gte": 1},
        "match": rebuilt_ret == 0 and rebuilt_channels >= 1
    })

    if rebuilt_ret != 0 or rebuilt_channels < 1:
        results["passed"] = False

    # Close file
    rebuilt.close_file()

    return results


def main():
    print("=" * 70)
    print("target.dll PARITY TEST - Original vs Rebuilt")
    print("=" * 70)
    print(f"Timestamp: {datetime.now().isoformat()}")
    print(f"Original DLL: {ORIGINAL_DLL}")
    print(f"Rebuilt DLL:  {REBUILT_DLL}")
    print()

    # Create results directory
    RESULTS_DIR.mkdir(exist_ok=True)

    all_results = []
    passed_count = 0
    failed_count = 0

    try:
        # Load DLLs
        print("Loading DLLs...")
        original = DLLWrapper(ORIGINAL_DLL, "original")
        rebuilt = DLLWrapper(REBUILT_DLL, "rebuilt")
        print("  [OK] Both DLLs loaded successfully")
        print()

        # Test versions
        print("=== Version Tests ===")
        result = test_versions(original, rebuilt)
        all_results.append(result)
        for detail in result["details"]:
            status = "[OK]" if detail["match"] else "[FAIL]"
            print(f"  {status} {detail['function']}: original={detail['original']}, rebuilt={detail['rebuilt']}")
        if result["passed"]:
            passed_count += 1
        else:
            failed_count += 1
        print()

        # Test init (with full 3-phase for original)
        print("=== Init Tests ===")
        result = test_init(original, rebuilt)
        all_results.append(result)

        original_init_success = False
        for detail in result["details"]:
            if "rebuilt_ok" in detail:
                status = "[OK]" if detail["rebuilt_ok"] else "[FAIL]"
                print(f"  {status} {detail['function']}: {detail['rebuilt_magic']}")
            if "original_success" in detail:
                original_init_success = detail["original_success"]
                status = "[OK]" if detail["original_success"] else "[FAIL]"
                print(f"  {status} {detail['function']}: {detail['original_message']}")

        if result["passed"]:
            passed_count += 1
        else:
            failed_count += 1
        print()

        # Test file I/O - try parity test if original init succeeded, else rebuilt only
        test_files = list(TEST_FILES_DIR.glob("*"))

        if original_init_success:
            print("=== File I/O PARITY Tests (Original vs Rebuilt) ===")
            print("  NOTE: Original DLL fully initialized with 3-phase protocol")
            print()

            for test_file in test_files:
                if test_file.is_file():
                    result = test_file_io_parity(original, rebuilt, test_file)
                    all_results.append(result)

                    for detail in result["details"]:
                        match = detail.get("match", False)
                        status = "[OK]" if match else "[FAIL]"
                        if "original" in detail and "rebuilt" in detail:
                            print(f"  {status} {detail['function']} ({test_file.name}): orig={detail['original']}, rebuilt={detail['rebuilt']}")

                    if result["passed"]:
                        passed_count += 1
                    else:
                        failed_count += 1
        else:
            print("=== File I/O Tests (Rebuilt DLL Only) ===")
            print("  NOTE: Original DLL 3-phase init failed, testing rebuilt only")
            print()

            for test_file in test_files:
                if test_file.is_file():
                    result = test_file_io_rebuilt_only(rebuilt, test_file)
                    all_results.append(result)

                    for detail in result["details"]:
                        match = detail.get("match", False)
                        status = "[OK]" if match else "[FAIL]"
                        if "rebuilt" in detail:
                            rebuilt_val = detail["rebuilt"]
                            expected_val = detail.get("expected", "N/A")
                            print(f"  {status} {detail['function']} ({test_file.name}): rebuilt={rebuilt_val}, expected={expected_val}")

                    if result["passed"]:
                        passed_count += 1
                    else:
                        failed_count += 1
        print()

    except Exception as e:
        print(f"[ERROR] {e}")
        import traceback
        traceback.print_exc()
        failed_count += 1

    # Summary
    print("=" * 70)
    print("SUMMARY")
    print("=" * 70)
    total = passed_count + failed_count
    print(f"  Passed: {passed_count}/{total}")
    print(f"  Failed: {failed_count}/{total}")
    print()

    # Save results
    results_file = RESULTS_DIR / "parity_results.json"
    with open(results_file, "w") as f:
        json.dump({
            "timestamp": datetime.now().isoformat(),
            "passed": passed_count,
            "failed": failed_count,
            "total": total,
            "results": all_results
        }, f, indent=2)
    print(f"Results saved to: {results_file}")

    # Exit code
    if failed_count > 0:
        print("\n[FAIL] Some tests failed!")
        return 1
    else:
        print("\n[OK] All tests passed!")
        return 0


if __name__ == "__main__":
    sys.exit(main())
