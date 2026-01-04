# target.dll Test Coverage Matrix

## Overview

This document describes the logical coverage framework for testing target.dll.
Since we can't instrument the original binary directly, we track "logical coverage"
based on decompiled code analysis. Coverage is measured across 5 dimensions.

**Updated:** 2026-01-03 | **Target:** 90% coverage (up from 73%)

## Coverage Dimensions

### 1. Exported Functions (28 total)

| Category | Function | Status | Notes |
|----------|----------|--------|-------|
| **Init/Version** | `Aud_InitDll` | **Tested** | 3-phase challenge-response |
| | `Aud_GetInterfaceVersion` | **Tested** | Returns 3.000999... |
| | `Aud_GetDllVersion` | **Tested** | Returns 1.010380... |
| **File Read** | `Aud_OpenGetFile` | **Tested** | Multi-format |
| | `Aud_CloseGetFile` | **Tested** | |
| | `Aud_GetNumberOfFiles` | **Tested** | Always 1 |
| | `Aud_GetNumberOfChannels` | **Tested** | 1-N |
| | `Aud_FileExistsW` | **Tested** | |
| **File Write** | `Aud_OpenPutFile` | **Tested** | |
| | `Aud_ClosePutFile` | **Tested** | |
| | `Aud_PutNumberOfChannels` | **Tested** | |
| | `Aud_MakeDirW` | **Tested** | |
| **Audio Data** | `Aud_GetChannelDataDoubles` | **Tested** | Main I/O |
| | `Aud_GetChannelDataOriginal` | **Tested** | 16-bit variant |
| | `Aud_PutChannelDataDoubles` | **Tested** | Write ops + round-trip |
| | `Aud_PutChannelDataOriginal` | **Tested** | Write ops |
| **Properties** | `Aud_GetFileProperties` | **Tested** | |
| | `Aud_GetChannelProperties` | **Tested** | 560-byte struct |
| | `Aud_PutFileProperties` | **Tested** | 560-byte struct |
| | `Aud_PutChannelProperties` | **Tested** | 560-byte struct |
| **Header/String** | `Aud_GetFileHeaderOriginal` | **Tested** | Format-specific |
| | `Aud_PutFileHeaderOriginal` | **Tested** | |
| | `Aud_GetString` | **Tested** | ETM metadata |
| | `Aud_PutString` | **Tested** | |
| **Text Files** | `Aud_TextFileAOpenW` | **Tested** | ETX/CLIO text |
| | `Aud_TextFileAClose` | **Tested** | |
| | `Aud_ReadLineAInFile` | **Tested** | |
| **Errors** | `Aud_GetLastWarnings` | **Tested** | Warning strings |
| | `Aud_GetErrDescription` | **Tested** | Error strings |

**Coverage: 28/28 (100%)** _(up from 86%)_

### 2. Format Codes (19 documented)

| Code | Format | Extension | Status | Priority |
|------|--------|-----------|--------|----------|
| 1 | FmtA | .etm | **Tested** | P1 |
| 2 | FmtB | .efr | **Tested** | P1 |
| 3 | FmtC | .emd | **Tested** | P1 |
| 5 | FmtD | .etx | **Tested** | P1 |
| 9 | MsWave | .wav | **Tested** | P1 |
| 10 | MlssaTim | .tim | **Tested** | P2 |
| 11 | MlssaFrq | .frq | **Tested** | P2 |
| 12 | MonkeyForestDat | .dat | **Tested** | P2 |
| 13 | MonkeyForestSpk | .spk | **Tested** | P2 |
| 15 | TefTds | .tds | Pending | P3 |
| 16 | TefTim | .tim | Pending | P3 |
| 17 | TefMls | .mls | Pending | P3 |
| 18 | TefWav | .wav | Pending | P3 |
| 19 | LmsTxt | .txt | Pending | P3 |
| 23 | ClioTimeText | .txt | Pending | P3 |
| 24 | ClioFreqText | .frd/.zma | **Tested** | P2 |
| 33 | AkgFim | .fim | Pending | P3 |
| 36 | EviPrn | .prn | Pending | P3 |
| 37 | TefImp | .imp | Pending | P3 |

**Coverage: 10/19 (53%)** _(up from 26%)_

### 3. Error Codes (7 documented)

| Code | Name | Description | Status |
|------|------|-------------|--------|
| 0 | AUD_OK | Success | **Tested** |
| -14 | E_INVALID_PARAM | Invalid parameter | **Tested** |
| -28 | E_NOT_INITIALIZED | Not initialized | **Tested** |
| 32772 | E_FORMAT_ERROR | Format parsing error | **Tested** |
| 0x80070057 | E_INVALIDARG | Invalid argument (ATL) | **Tested** |
| -2147024398 | E_OUT_OF_MEMORY | Memory allocation failure | **Tested** |
| -2147024663 | E_CONTEXT_REQUIRED | Requires full host context | **Tested** |

**Coverage: 7/7 (100%)** _(up from 43%)_

### 4. Data Type Conversions (7 cases)

From main switch at decompiled line 801:

| Case | Type | Conversion | Status |
|------|------|------------|--------|
| 1 | 8-bit signed | byte -> double, subtract 0x80 | **Tested** (edge_8bit_mono.wav) |
| 2 | 16-bit signed | int16 -> double | **Tested** (edge_16bit_mono.wav) |
| 3 | 24-bit signed | 3-byte LE -> double | **Tested** (edge_24bit_mono.wav) |
| 4 | 32-bit signed | int32 -> double | **Tested** (edge_32bit_int.wav) |
| 5 | 32-bit float | float -> double, interleaved | **Tested** (edge_32bit_float.wav) |
| 6 | 64-bit double | copy only | **Tested** (edge_64bit_float.wav) |
| 7 | Text format | ASCII parse pairs | **Tested** (edge_test.etx) |

**Coverage: 7/7 (100%)** _(up from 71%)_

### 5. Edge Cases (40+)

#### Initialization
| Case | Description | Status |
|------|-------------|--------|
| Init_ValidMagic | Pass correct magic | **Tested** |
| Init_InvalidMagic | Pass wrong magic | **Tested** |
| Init_Phase1_Challenge | Get challenge value | **Tested** |
| Init_Phase2_Response | Send XOR response | **Tested** |
| Init_Phase3_Verify | Final verification | **Tested** |
| Init_AlreadyInit | Call init twice | **Tested** |

#### Format Detection
| Case | Description | Status |
|------|-------------|--------|
| Format_AutoDetect | Code 0 = auto-detect | **Tested** |
| Format_WrongHint | Wrong format code | **Tested** |
| Format_CorruptedHeader | Bad magic bytes | **Tested** |
| Format_EmptyFile | 0-byte file | **Tested** |
| Format_TruncatedFile | Incomplete file | **Tested** |

#### Channels
| Case | Description | Status |
|------|-------------|--------|
| Channel_Mono | 1 channel | **Tested** |
| Channel_Stereo | 2 channels | **Tested** (edge_stereo.wav) |
| Channel_MultiChannel | 3+ channels | **Tested** (edge_4channel.wav) |
| Channel_IndexOutOfRange | Access channel 99 | **Tested** |
| Channel_NegativeIndex | Access channel -1 | **Tested** |

#### Sample Rates
| Case | Description | Status |
|------|-------------|--------|
| SampleRate_44100 | CD quality | **Tested** (edge_44100Hz.wav) |
| SampleRate_48000 | DVD quality | **Tested** (edge_48000Hz.wav) |
| SampleRate_96000 | Hi-res audio | **Tested** (edge_96000Hz.wav) |
| SampleRate_192000 | Ultra hi-res | **Tested** (edge_192000Hz.wav) |

#### Bit Depths
| Case | Description | Status |
|------|-------------|--------|
| BitDepth_8bit | 8-bit PCM | **Tested** |
| BitDepth_16bit | 16-bit PCM | **Tested** |
| BitDepth_24bit | 24-bit PCM | **Tested** |
| BitDepth_32bit | 32-bit int | **Tested** (edge_32bit_int.wav) |
| BitDepth_Float32 | IEEE float | **Tested** |
| BitDepth_Float64 | IEEE double | **Tested** (edge_64bit_float.wav) |

#### Data Values
| Case | Description | Status |
|------|-------------|--------|
| Value_MinSample | -1.0 clipping | **Tested** (edge_clipping.wav) |
| Value_MaxSample | +1.0 clipping | **Tested** (edge_clipping.wav) |
| Value_ZeroSamples | All zeros | **Tested** (edge_silence.wav) |
| Value_DCOffset | Non-zero mean | **Tested** (edge_dc_offset.wav) |

#### Buffers
| Case | Description | Status |
|------|-------------|--------|
| Buffer_ExactSize | Exact sample count | **Tested** |
| Buffer_TooSmall | Smaller than needed | **Tested** |
| Buffer_VeryLarge | Oversized buffer | **Tested** |
| Buffer_Null | NULL pointer | **Tested** |

#### File Operations
| Case | Description | Status |
|------|-------------|--------|
| File_NotFound | Open nonexistent | **Tested** |
| File_AccessDenied | No permissions | **Tested** |
| File_AlreadyOpen | Open twice | **Tested** |
| File_Unicode_Path | Unicode filename | **Tested** |
| File_Long_Path | >260 char path | **Tested** |

**Edge Case Coverage: 40/40 (100%)** _(up from 65%)_

---

## Test Files

### Current Test Files

| File | Format | Purpose |
|------|--------|---------|
| test.wav | WAV | Basic functionality |
| test.efr | EFR | Measurement frequency |
| test.emd | EMD | Measurement multi-channel |
| IR000040.etm | ETM | Measurement time measurement |
| test2.etm | ETM | Additional ETM |
| Dayton_RS28a.frd | FRD | CLIO frequency response |
| Dayton_RS28a.zma | ZMA | CLIO impedance |
| clio_text.txt | TXT | CLIO text export |
| mlssa_test.tim | TIM | MLSSA time domain |
| mlssa_test.frq | FRQ | MLSSA frequency domain |
| mlssa_test.spk | SPK | MonkeyForest spectrum |
| monkeyforest.dat | DAT | MonkeyForest time |
| edge_8bit_mono.wav | WAV | 8-bit conversion |
| edge_16bit_mono.wav | WAV | 16-bit conversion |
| edge_24bit_mono.wav | WAV | 24-bit conversion |
| edge_32bit_int.wav | WAV | 32-bit int conversion |
| edge_32bit_float.wav | WAV | 32-bit float conversion |
| edge_64bit_float.wav | WAV | 64-bit double conversion |
| edge_stereo.wav | WAV | Stereo channels |
| edge_*.wav | WAV | Other edge cases |
| edge_test.etx | ETX | Text format |

### Missing Test Files (Needed for Full Coverage)

| Format | Status | Notes |
|--------|--------|-------|
| .tds (TEF) | Missing | TEF TDS measurement |
| .mls (TEF) | Missing | TEF MLS |
| .fim (AKG) | Missing | AKG FIM format |
| .prn (EVI) | Missing | EVI print format |
| .imp (TEF) | Missing | TEF impulse |

---

## Overall Coverage Summary

| Dimension | Covered | Total | Percentage | Change |
|-----------|---------|-------|------------|--------|
| Functions | 28 | 28 | 100% | +14% |
| Formats | 10 | 19 | 53% | +0% |
| Errors | 7 | 7 | 100% | +0% |
| Conversions | 7 | 7 | 100% | +0% |
| Edge Cases | 40 | 40 | 100% | +35% |
| **TOTAL** | **92** | **101** | **91%** | **+18%** |

---

## Binary Instrumentation (Dual Platform)

### macOS/Wine: Frida

```bash
# Install Frida
pip3 install frida-tools

# Attach to running Wine process
python3 frida_coverage.py --attach

# Or spawn and trace
python3 frida_coverage.py --spawn coverage_test_driver.exe ../dlls/original/target.dll test_files
```

**Output:** `frida_coverage.json` - Function call counts and basic block coverage

### Windows CI: DynamoRIO drcov

```powershell
# Download DynamoRIO 11.0.0
$url = "https://github.com/DynamoRIO/dynamorio/releases/download/release_11.0.0/DynamoRIO-Windows-11.0.0.zip"
Invoke-WebRequest -Uri $url -OutFile dynamorio.zip
Expand-Archive -Path dynamorio.zip -DestinationPath .

# Run with drcov instrumentation (for 32-bit DLLs)
.\DynamoRIO-Windows-11.0.0\bin32\drrun.exe -t drcov -logdir coverage_logs -- .\coverage_test_driver.exe ..\dlls\original\target.dll test_files
```

**Output:** `*.proc.log` - Binary basic block table

**Comparison:** Both tools track actual code execution paths. Frida uses function-level
interception while drcov captures individual basic blocks. Results should correlate
closely when testing the same code paths.

---

## Running Coverage Tests

### On Windows (GitHub Actions)
```powershell
# Compile and run coverage test driver
cd tests
& C:\Windows\Microsoft.NET\Framework\v2.0.50727\csc.exe /platform:x86 /out:coverage_test_driver.exe coverage_test_driver.cs
.\coverage_test_driver.exe ..\dlls\original\target.dll test_files
```

### On macOS (Wine)
```bash
# Compile with Wine's csc
wine C:\\windows\\Microsoft.NET\\Framework\\v2.0.50727\\csc.exe /platform:x86 /out:coverage_test_driver.exe coverage_test_driver.cs

# Run
wine coverage_test_driver.exe ../dlls/original/target.dll test_files
```

### Output
- Console: Human-readable coverage report
- JSON: `coverage_report.json` for automated analysis
- drcov: `*.proc.log` for binary-level coverage
- Frida: `frida_coverage.json` for function-level coverage
