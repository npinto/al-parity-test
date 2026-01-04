# target.dll Parity Test

Automated parity testing between original and rebuilt target.dll on Windows via GitHub Actions.

## Coverage Metrics

| Metric | Value | Notes |
|--------|-------|-------|
| **Export Function Coverage** | 100% (29/29) | All exports exercised |
| **Application Code Coverage** | 69.5% | Export functions only (excluding CRT) |
| **.text Section Coverage** | 16.5% | Low due to static CRT linking |
| **Basic Blocks Executed** | 2,207 | In target DLL module |

**Notes:**
- Export function coverage is the primary metric - all 29 API functions are tested
- Application code coverage measures only our code, excluding ~130KB of CRT startup code
- DynamoRIO binary instrumentation crashes on some edge cases, limiting coverage of error paths
- Remaining uncovered code is primarily in error handling branches

## Structure

```
github_parity_test/
├── .github/workflows/parity_test.yml  # GitHub Actions workflow
├── dlls/
│   ├── original/target.dll          # Original DLL
│   └── rebuilt/target.dll           # Our rebuilt C clone
├── tests/
│   ├── parity_test.py                 # Python test script
│   ├── dotnet_host.cs                 # .NET 2.0 parity test host
│   ├── coverage_test_driver.cs        # Coverage test driver
│   ├── test_files/                    # Test audio files (38 files)
│   └── results/                       # Test output (generated)
├── analyze_drcov.py                   # Binary coverage analyzer
└── README.md
```

## Test Files

| Category | Files | Description |
|----------|-------|-------------|
| WAV formats | 16 | 8/16/24/32-bit, mono/stereo, various sample rates |
| Measurement formats | 5 | ETM, EFR, EMD, ETX |
| CLIO formats | 3 | FRD, ZMA, TXT |
| MLSSA formats | 3 | TIM, FRQ, SPK |
| MonkeyForest | 1 | DAT |
| Malformed files | 10 | Edge cases for error handling |

## Usage

1. Create a new **private** GitHub repository
2. Push this folder contents to the repo
3. Tests run automatically on push
4. View results in Actions tab

## Manual Trigger

Go to Actions → "DLL Parity Test" → "Run workflow"

## Results

Test results are saved as artifacts in each workflow run:
- `test-results/`: Parity test output for each file
- `drcov-coverage/`: DynamoRIO binary coverage logs
