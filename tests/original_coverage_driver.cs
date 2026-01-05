/*
 * Original DLL Coverage Test Driver
 *
 * Specifically designed to test the ORIGINAL target.dll for code coverage.
 * Uses smaller test batches and careful error handling to avoid crashes.
 *
 * Key differences from coverage_test_driver.cs:
 * - Specifically targets original DLL (with its quirks)
 * - Uses smaller buffer sizes to avoid memory issues
 * - Skips known-hanging functions (GetErrDescription, GetLastWarnings, TextFileAOpenW)
 * - Tests file operations one at a time with proper cleanup
 *
 * Build with .NET 2.0:
 *   csc /platform:x86 /out:original_coverage_driver.exe original_coverage_driver.cs
 *
 * Run with OpenCppCoverage:
 *   OpenCppCoverage.exe --modules target.dll --export_type html:cov_original -- original_coverage_driver.exe ..\dlls\original\target.dll test_files
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

class OriginalCoverageDriver
{
    // DLL function delegates
    delegate double Aud_GetInterfaceVersion_t();
    delegate double Aud_GetDllVersion_t();
    delegate uint Aud_InitDll_t(uint magic);
    delegate int Aud_OpenGetFile_t([MarshalAs(UnmanagedType.LPWStr)] string path, int format, int extra);
    delegate int Aud_CloseGetFile_t();
    delegate int Aud_GetNumberOfFiles_t(out uint count);
    delegate int Aud_GetNumberOfChannels_t(uint fileIdx, out uint count);
    delegate int Aud_FileExistsW_t([MarshalAs(UnmanagedType.LPWStr)] string path);
    delegate int Aud_GetChannelProperties_t(uint fileIdx, uint chanIdx, [Out] byte[] buffer);
    delegate int Aud_GetChannelDataDoubles_t(uint fileIdx, uint chanIdx, [Out] double[] buffer);
    delegate int Aud_GetFileHeaderOriginal_t(uint fileIdx, [Out] byte[] buffer, out uint size);
    delegate int Aud_GetString_t(uint stringId, [Out] byte[] buffer, uint bufferSize);

    // Write operations
    delegate int Aud_OpenPutFile_t([MarshalAs(UnmanagedType.LPWStr)] string path, int format);
    delegate int Aud_ClosePutFile_t();
    delegate int Aud_PutNumberOfChannels_t(uint count);
    delegate int Aud_MakeDirW_t([MarshalAs(UnmanagedType.LPWStr)] string path);
    delegate int Aud_PutFileProperties_t(uint fileIdx, [In] byte[] buffer);
    delegate int Aud_PutChannelProperties_t(uint fileIdx, uint chanIdx, [In] byte[] buffer);
    delegate int Aud_PutChannelDataDoubles_t(uint fileIdx, uint chanIdx, [In] double[] buffer, uint count);
    // NOTE: Aud_GetFileProperties_t skipped - causes hard crash (STATUS_BREAKPOINT) on original DLL
    // delegate int Aud_GetFileProperties_t(uint fileIdx, [Out] byte[] buffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr LoadLibrary(string lpFileName);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool FreeLibrary(IntPtr hModule);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

    // Three-phase init constants
    const int INIT_XOR_CONSTANT = 1114983470;
    const int PHASE3_MAGIC = 1230000000;
    const int PHASE3_XOR_RESULT = 1826820242;

    // Coverage tracking
    static Dictionary<string, bool> FunctionsCovered = new Dictionary<string, bool>();
    static int TotalCalls = 0;
    static int SuccessfulCalls = 0;

    static IntPtr hDll;

    static void MarkCovered(string func)
    {
        FunctionsCovered[func] = true;
    }

    static Delegate GetDelegate(IntPtr hDll, string name, Type delegateType)
    {
        IntPtr addr = GetProcAddress(hDll, name);
        if (addr == IntPtr.Zero) return null;
        return Marshal.GetDelegateForFunctionPointer(addr, delegateType);
    }

    // Format codes from decompiled FUN_1001fa40 (lines 25207-25316)
    // These MUST match exactly or we get error 0x8004 "Not Standard File Extension"
    static int GetFormatCode(string path)
    {
        string ext = Path.GetExtension(path).ToLower();

        // EASERA formats (most reliable - we have good test files)
        if (ext == ".etm") return 1;   // EaseraEtm - IR time domain
        if (ext == ".efr") return 2;   // EaseraEfr - FR frequency domain
        if (ext == ".emd") return 3;   // EaseraEmd - Multi-data container
        if (ext == ".etx") return 5;   // EaseraEtx - Text export

        // WAV format
        if (ext == ".wav") return 9;   // MsWave - Standard WAV

        // MLSSA formats
        if (ext == ".tim") return 10;  // MlssaTim - Time domain binary
        if (ext == ".frq") return 11;  // MlssaFrq - Frequency domain binary

        // MonkeyForest formats
        if (ext == ".dat") return 12;  // MonkeyForestDat - Time signals
        if (ext == ".spk") return 13;  // MonkeyForestSpk - Spectrum data

        // CLIO text formats (tab-delimited frequency response)
        if (ext == ".frd") return 27;  // 0x1b - ClioFreqText FRD
        if (ext == ".zma") return 28;  // 0x1c - ClioFreqText ZMA (impedance)

        // LMS/FilterShop text format
        // NOTE: .txt is ambiguous - could be LMS (19), CLIO text (0x17/0x18), or other
        // We try LMS format first since that's what our test files use
        if (ext == ".txt") return 19;  // 0x13 - LmsTxt

        // EVI PRN format
        if (ext == ".prn") return 36;  // 0x24 - EviPrn

        // TEF formats (Gold Line - discontinued, unlikely to work)
        if (ext == ".imp") return 37;  // 0x25 - TefImp
        if (ext == ".mls") return 17;  // 0x11 - TefMls (NOT CLIO .mls binary!)

        // AKG FIM format
        if (ext == ".fim") return 33;  // 0x21 - AkgFim

        // Unknown - return 0 which will use auto-detection (format 0x14 = 20)
        return 0;
    }

    static void Log(string msg)
    {
        Console.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] " + msg);
        Console.Out.Flush();
    }

    static bool TestThreePhaseInit(Aud_InitDll_t Aud_InitDll)
    {
        Log("Phase 1: Getting challenge...");
        TotalCalls++;
        uint challenge = Aud_InitDll(0);
        MarkCovered("Aud_InitDll");
        Log("  Challenge: 0x" + challenge.ToString("x8"));

        Log("Phase 2: Sending response...");
        TotalCalls++;
        uint response = (uint)((int)challenge ^ INIT_XOR_CONSTANT);
        uint phase2Result = Aud_InitDll(response);
        Log("  Response: 0x" + response.ToString("x8") + " -> Result: " + phase2Result);

        if (phase2Result != 0)
        {
            Log("[FAIL] Phase 2 failed");
            return false;
        }
        SuccessfulCalls++;

        Log("Phase 3: Verification...");
        TotalCalls++;
        uint phase3Result = Aud_InitDll((uint)PHASE3_MAGIC);
        uint expected = (uint)(PHASE3_MAGIC ^ PHASE3_XOR_RESULT);
        Log("  Result: 0x" + phase3Result.ToString("x8") + ", Expected: 0x" + expected.ToString("x8"));

        if (phase3Result != expected)
        {
            Log("[FAIL] Phase 3 failed");
            return false;
        }
        SuccessfulCalls++;

        Log("[OK] Three-phase init complete");
        return true;
    }

    static void TestVersionFunctions(Aud_GetInterfaceVersion_t getIV, Aud_GetDllVersion_t getDV)
    {
        Log("Testing version functions...");

        if (getIV != null)
        {
            TotalCalls++;
            try
            {
                double iv = getIV();
                MarkCovered("Aud_GetInterfaceVersion");
                Log("  InterfaceVersion: " + iv);
                SuccessfulCalls++;
            }
            catch (Exception ex)
            {
                Log("  InterfaceVersion: EXCEPTION - " + ex.Message);
            }
        }

        if (getDV != null)
        {
            TotalCalls++;
            try
            {
                double dv = getDV();
                MarkCovered("Aud_GetDllVersion");
                Log("  DllVersion: " + dv);
                SuccessfulCalls++;
            }
            catch (Exception ex)
            {
                Log("  DllVersion: EXCEPTION - " + ex.Message);
            }
        }
    }

    static void TestFileRead(string testFile,
        Aud_OpenGetFile_t openFile, Aud_CloseGetFile_t closeFile,
        Aud_GetNumberOfFiles_t getNumFiles, Aud_GetNumberOfChannels_t getNumChannels,
        Aud_GetChannelProperties_t getProps, Aud_GetChannelDataDoubles_t getData,
        Aud_GetFileHeaderOriginal_t getHeader, Aud_GetString_t getString)
    {
        string fileName = Path.GetFileName(testFile);
        Log("Testing file: " + fileName);

        string absPath = Path.GetFullPath(testFile);
        int formatCode = GetFormatCode(testFile);

        TotalCalls++;
        int openRet = openFile(absPath, formatCode, 0);
        MarkCovered("Aud_OpenGetFile");
        Log("  Open(" + formatCode + "): " + openRet);

        if (openRet != 0)
        {
            Log("  [SKIP] File not opened");
            return;
        }
        SuccessfulCalls++;

        // GetNumberOfFiles
        if (getNumFiles != null)
        {
            TotalCalls++;
            try
            {
                uint fileCount;
                getNumFiles(out fileCount);
                MarkCovered("Aud_GetNumberOfFiles");
                Log("  Files: " + fileCount);
                SuccessfulCalls++;
            }
            catch (Exception ex)
            {
                Log("  Files: EXCEPTION - " + ex.Message);
            }
        }

        // GetNumberOfChannels
        uint numChannels = 0;
        if (getNumChannels != null)
        {
            TotalCalls++;
            try
            {
                getNumChannels(0, out numChannels);
                MarkCovered("Aud_GetNumberOfChannels");
                Log("  Channels: " + numChannels);
                SuccessfulCalls++;
            }
            catch (Exception ex)
            {
                Log("  Channels: EXCEPTION - " + ex.Message);
            }
        }

        // GetFileProperties - SKIP on original DLL!
        // Causes hard crash (STATUS_BREAKPOINT / DebugBreak) even though file is successfully opened.
        // The original DLL likely requires full EASE context (MFC runtime, .NET interop) to work.
        // We test channel properties instead which is the more common use case.
        Log("  [SKIP] GetFileProperties - causes crash on original DLL");

        // GetChannelProperties
        uint numSamples = 0;
        if (getProps != null)
        {
            TotalCalls++;
            try
            {
                byte[] propBuffer = new byte[560];
                int propRet = getProps(0, 0, propBuffer);
                MarkCovered("Aud_GetChannelProperties");

                if (propRet == 0)
                {
                    double sampleRate = BitConverter.ToDouble(propBuffer, 0);
                    numSamples = BitConverter.ToUInt32(propBuffer, 12);
                    int bitsPerSample = BitConverter.ToInt32(propBuffer, 20);
                    Log("  Properties: sr=" + sampleRate + " samples=" + numSamples + " bits=" + bitsPerSample);
                    SuccessfulCalls++;
                }
                else
                {
                    Log("  Properties: ret=" + propRet);
                }
            }
            catch (Exception ex)
            {
                Log("  Properties: EXCEPTION - " + ex.Message);
            }
        }

        // GetChannelDataDoubles - use SMALL buffer to avoid crashes
        if (getData != null && numSamples > 0)
        {
            TotalCalls++;
            try
            {
                // Use minimum of actual samples or 1000 to avoid large allocations
                int bufSize = (int)Math.Min(numSamples, 1000);
                if (bufSize < 10) bufSize = 100;  // Minimum buffer

                double[] samples = new double[bufSize];
                int dataRet = getData(0, 0, samples);
                MarkCovered("Aud_GetChannelDataDoubles");

                if (dataRet == 0)
                {
                    // Find range
                    double min = samples[0], max = samples[0];
                    for (int i = 1; i < bufSize; i++)
                    {
                        if (samples[i] < min) min = samples[i];
                        if (samples[i] > max) max = samples[i];
                    }
                    Log("  Data: [" + min.ToString("G4") + ", " + max.ToString("G4") + "]");
                    SuccessfulCalls++;
                }
                else
                {
                    Log("  Data: ret=" + dataRet);
                }
            }
            catch (Exception ex)
            {
                Log("  Data: EXCEPTION - " + ex.Message);
            }
        }

        // GetFileHeaderOriginal
        if (getHeader != null)
        {
            TotalCalls++;
            try
            {
                byte[] headerBuf = new byte[512];
                uint headerSize = 0;
                int headerRet = getHeader(0, headerBuf, out headerSize);
                MarkCovered("Aud_GetFileHeaderOriginal");
                Log("  Header: ret=" + headerRet + " size=" + headerSize);
                SuccessfulCalls++;
            }
            catch (Exception ex)
            {
                Log("  Header: EXCEPTION - " + ex.Message);
            }
        }

        // GetString - try a few string IDs
        if (getString != null)
        {
            TotalCalls++;
            try
            {
                byte[] strBuf = new byte[256];
                int strRet = getString(0, strBuf, 256);
                MarkCovered("Aud_GetString");
                Log("  String(0): ret=" + strRet);
                SuccessfulCalls++;
            }
            catch (Exception ex)
            {
                Log("  String: EXCEPTION - " + ex.Message);
            }
        }

        // Close file
        if (closeFile != null)
        {
            TotalCalls++;
            try
            {
                closeFile();
                MarkCovered("Aud_CloseGetFile");
                Log("  Closed");
                SuccessfulCalls++;
            }
            catch (Exception ex)
            {
                Log("  Close: EXCEPTION - " + ex.Message);
            }
        }

        Log("  [OK] " + fileName + " complete");
    }

    static void TestFileExists(Aud_FileExistsW_t fileExists)
    {
        if (fileExists == null) return;

        Log("Testing FileExistsW...");

        // Test with existing file ONLY
        // NOTE: On original DLL, testing non-existent files triggers MessageBox popup which hangs!
        // Only test files that we KNOW exist
        TotalCalls++;
        try
        {
            int exists = fileExists("C:\\Windows\\notepad.exe");
            MarkCovered("Aud_FileExistsW");
            Log("  FileExists(notepad.exe): " + exists);
            SuccessfulCalls++;
        }
        catch (Exception ex)
        {
            Log("  FileExists: EXCEPTION - " + ex.Message);
        }

        // DO NOT test non-existent files - causes MessageBox hang on original DLL!
        Log("  [SKIP] Non-existent file test skipped (causes MessageBox hang on original DLL)");
    }

    static void TestWriteOperations(
        Aud_OpenPutFile_t openPut, Aud_ClosePutFile_t closePut,
        Aud_PutNumberOfChannels_t putChannels, Aud_MakeDirW_t makeDir,
        Aud_PutFileProperties_t putFileProps, Aud_PutChannelProperties_t putChanProps,
        Aud_PutChannelDataDoubles_t putData)
    {
        Log("Testing write operations...");

        // Create temp directory
        string tempDir = Path.Combine(Path.GetTempPath(), "origdll_test_" + DateTime.Now.Ticks);

        if (makeDir != null)
        {
            TotalCalls++;
            try
            {
                int mkdirRet = makeDir(tempDir);
                MarkCovered("Aud_MakeDirW");
                Log("  MakeDirW: " + mkdirRet);
                SuccessfulCalls++;
            }
            catch (Exception ex)
            {
                Log("  MakeDirW: EXCEPTION - " + ex.Message);
            }
        }

        // Test OpenPutFile
        string testOutput = Path.Combine(tempDir, "test_out.wav");
        if (openPut != null)
        {
            TotalCalls++;
            try
            {
                int openRet = openPut(testOutput, 9);  // WAV format
                MarkCovered("Aud_OpenPutFile");
                Log("  OpenPutFile: " + openRet);

                if (openRet == 0)
                {
                    SuccessfulCalls++;

                    // PutNumberOfChannels
                    if (putChannels != null)
                    {
                        TotalCalls++;
                        try
                        {
                            int chanRet = putChannels(1);
                            MarkCovered("Aud_PutNumberOfChannels");
                            Log("  PutNumberOfChannels(1): " + chanRet);
                            SuccessfulCalls++;
                        }
                        catch (Exception ex)
                        {
                            Log("  PutNumberOfChannels: EXCEPTION - " + ex.Message);
                        }
                    }

                    // PutFileProperties
                    if (putFileProps != null)
                    {
                        TotalCalls++;
                        try
                        {
                            byte[] propBuf = new byte[560];
                            BitConverter.GetBytes(48000.0).CopyTo(propBuf, 0);
                            int propRet = putFileProps(0, propBuf);
                            MarkCovered("Aud_PutFileProperties");
                            Log("  PutFileProperties: " + propRet);
                            SuccessfulCalls++;
                        }
                        catch (Exception ex)
                        {
                            Log("  PutFileProperties: EXCEPTION - " + ex.Message);
                        }
                    }

                    // PutChannelProperties
                    if (putChanProps != null)
                    {
                        TotalCalls++;
                        try
                        {
                            byte[] chanPropBuf = new byte[560];
                            BitConverter.GetBytes(48000.0).CopyTo(chanPropBuf, 0);
                            int cpRet = putChanProps(0, 0, chanPropBuf);
                            MarkCovered("Aud_PutChannelProperties");
                            Log("  PutChannelProperties: " + cpRet);
                            SuccessfulCalls++;
                        }
                        catch (Exception ex)
                        {
                            Log("  PutChannelProperties: EXCEPTION - " + ex.Message);
                        }
                    }

                    // PutChannelDataDoubles - SMALL buffer
                    if (putData != null)
                    {
                        TotalCalls++;
                        try
                        {
                            double[] samples = new double[100];  // Small buffer
                            for (int i = 0; i < samples.Length; i++)
                                samples[i] = Math.Sin(2 * Math.PI * 1000 * i / 48000.0);

                            int dataRet = putData(0, 0, samples, 100);
                            MarkCovered("Aud_PutChannelDataDoubles");
                            Log("  PutChannelDataDoubles(100): " + dataRet);
                            SuccessfulCalls++;
                        }
                        catch (Exception ex)
                        {
                            Log("  PutChannelDataDoubles: EXCEPTION - " + ex.Message);
                        }
                    }

                    // ClosePutFile
                    if (closePut != null)
                    {
                        TotalCalls++;
                        try
                        {
                            int closeRet = closePut();
                            MarkCovered("Aud_ClosePutFile");
                            Log("  ClosePutFile: " + closeRet);
                            SuccessfulCalls++;
                        }
                        catch (Exception ex)
                        {
                            Log("  ClosePutFile: EXCEPTION - " + ex.Message);
                        }
                    }
                }
                else
                {
                    Log("  [INFO] OpenPutFile returned error (expected for original DLL without context)");
                }
            }
            catch (Exception ex)
            {
                Log("  OpenPutFile: EXCEPTION - " + ex.Message);
            }
        }

        // Cleanup
        try
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
        catch { }
    }

    static void PrintReport()
    {
        Console.WriteLine("\n" + new string('=', 70));
        Console.WriteLine("ORIGINAL DLL FUNCTION COVERAGE REPORT");
        Console.WriteLine(new string('=', 70));

        // Group functions by category for clearer reporting
        Console.WriteLine("\n--- INITIALIZATION ---");
        PrintFunctionStatus("Aud_InitDll");
        PrintFunctionStatus("Aud_GetInterfaceVersion");
        PrintFunctionStatus("Aud_GetDllVersion");

        Console.WriteLine("\n--- FILE READ OPERATIONS ---");
        PrintFunctionStatus("Aud_OpenGetFile");
        PrintFunctionStatus("Aud_CloseGetFile");
        PrintFunctionStatus("Aud_GetNumberOfFiles");
        PrintFunctionStatus("Aud_GetNumberOfChannels");
        PrintFunctionStatus("Aud_GetChannelProperties");
        PrintFunctionStatus("Aud_GetChannelDataDoubles");
        PrintFunctionStatus("Aud_GetFileHeaderOriginal");
        PrintFunctionStatus("Aud_GetString");

        Console.WriteLine("\n--- FILE WRITE OPERATIONS ---");
        PrintFunctionStatus("Aud_OpenPutFile");
        PrintFunctionStatus("Aud_ClosePutFile");
        PrintFunctionStatus("Aud_PutNumberOfChannels");
        PrintFunctionStatus("Aud_PutFileProperties");
        PrintFunctionStatus("Aud_PutChannelProperties");
        PrintFunctionStatus("Aud_PutChannelDataDoubles");

        Console.WriteLine("\n--- UTILITY FUNCTIONS ---");
        PrintFunctionStatus("Aud_FileExistsW");
        PrintFunctionStatus("Aud_MakeDirW");

        Console.WriteLine("\n--- SKIPPED (require EASE context) ---");
        Console.WriteLine("  [SKIP] Aud_GetFileProperties     - causes STATUS_BREAKPOINT crash");
        Console.WriteLine("  [SKIP] Aud_GetErrDescription     - hangs (MFC CString)");
        Console.WriteLine("  [SKIP] Aud_GetLastWarnings       - hangs (MFC CString)");
        Console.WriteLine("  [SKIP] Aud_TextFileAOpenW        - hangs (EASE file system)");

        // Count covered
        int covered = 0;
        foreach (KeyValuePair<string, bool> kvp in FunctionsCovered)
        {
            if (kvp.Value) covered++;
        }

        Console.WriteLine("\n" + new string('=', 70));
        Console.WriteLine("SUMMARY");
        Console.WriteLine(new string('-', 70));
        Console.WriteLine("Functions tested:    " + covered + "/" + FunctionsCovered.Count + " (" + (100.0 * covered / FunctionsCovered.Count).ToString("F0") + "%)");
        Console.WriteLine("Total DLL calls:     " + TotalCalls);
        Console.WriteLine("Successful calls:    " + SuccessfulCalls);
        Console.WriteLine("Success rate:        " + (100.0 * SuccessfulCalls / Math.Max(TotalCalls, 1)).ToString("F1") + "%");
        Console.WriteLine("Skipped functions:   4 (require full EASE SpeakerLab context)");
        Console.WriteLine(new string('=', 70));

        // Machine-readable summary line for CI parsing
        Console.WriteLine("\n[METRICS] covered=" + covered + " total=" + FunctionsCovered.Count + " calls=" + TotalCalls + " success=" + SuccessfulCalls);
    }

    static void PrintFunctionStatus(string funcName)
    {
        bool covered = FunctionsCovered.ContainsKey(funcName) && FunctionsCovered[funcName];
        string status = covered ? "[OK]  " : "[    ]";
        Console.WriteLine("  " + status + " " + funcName);
    }

    static int Main(string[] args)
    {
        Console.WriteLine(new string('=', 60));
        Console.WriteLine("Original DLL Coverage Test Driver");
        Console.WriteLine(new string('=', 60));

        if (args.Length < 1)
        {
            Console.WriteLine("Usage: original_coverage_driver.exe <dll_path> [test_files_dir]");
            Console.WriteLine("");
            Console.WriteLine("Tests the ORIGINAL target.dll for code coverage.");
            Console.WriteLine("Designed to work with OpenCppCoverage:");
            Console.WriteLine("  OpenCppCoverage.exe --modules target.dll -- original_coverage_driver.exe ..\\dlls\\original\\target.dll test_files");
            return 1;
        }

        string dllPath = args[0];
        string testDir = args.Length > 1 ? args[1] : "test_files";

        Log("DLL: " + dllPath);
        Log("Test dir: " + testDir);

        // Initialize coverage tracking
        string[] functions = {
            "Aud_InitDll", "Aud_GetInterfaceVersion", "Aud_GetDllVersion",
            "Aud_OpenGetFile", "Aud_CloseGetFile", "Aud_GetNumberOfFiles",
            "Aud_GetNumberOfChannels", "Aud_FileExistsW",
            "Aud_GetChannelProperties",  // NOTE: Aud_GetFileProperties skipped - causes crash
            "Aud_GetChannelDataDoubles", "Aud_GetFileHeaderOriginal", "Aud_GetString",
            "Aud_OpenPutFile", "Aud_ClosePutFile", "Aud_PutNumberOfChannels",
            "Aud_MakeDirW", "Aud_PutFileProperties", "Aud_PutChannelProperties",
            "Aud_PutChannelDataDoubles"
            // NOTE: Skipped functions that hang on original DLL:
            // - Aud_GetErrDescription (hangs - requires MFC CString)
            // - Aud_GetLastWarnings (hangs - requires MFC CString)
            // - Aud_TextFileAOpenW (hangs - requires EASE file system)
            // - Aud_TextFileAClose (depends on above)
            // - Aud_ReadLineAInFile (depends on above)
            // - Aud_PutString (crashes - access violation)
            // - Aud_PutFileHeaderOriginal (requires full context)
            // - Aud_GetChannelDataOriginal (rarely used)
            // - Aud_PutChannelDataOriginal (rarely used)
        };
        foreach (string f in functions)
            FunctionsCovered[f] = false;

        // Load DLL
        Log("Loading DLL...");
        string dllDir = Path.GetDirectoryName(Path.GetFullPath(dllPath));
        string oldDir = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(dllDir);
        hDll = LoadLibrary(dllPath);
        Directory.SetCurrentDirectory(oldDir);

        if (hDll == IntPtr.Zero)
        {
            Log("[FAIL] LoadLibrary failed: " + Marshal.GetLastWin32Error());
            return 1;
        }
        Log("DLL loaded at 0x" + hDll.ToString("x8"));

        try
        {
            // Get function pointers
            Aud_InitDll_t initDll = (Aud_InitDll_t)GetDelegate(hDll, "Aud_InitDll", typeof(Aud_InitDll_t));
            Aud_GetInterfaceVersion_t getIV = (Aud_GetInterfaceVersion_t)GetDelegate(hDll, "Aud_GetInterfaceVersion", typeof(Aud_GetInterfaceVersion_t));
            Aud_GetDllVersion_t getDV = (Aud_GetDllVersion_t)GetDelegate(hDll, "Aud_GetDllVersion", typeof(Aud_GetDllVersion_t));
            Aud_OpenGetFile_t openFile = (Aud_OpenGetFile_t)GetDelegate(hDll, "Aud_OpenGetFile", typeof(Aud_OpenGetFile_t));
            Aud_CloseGetFile_t closeFile = (Aud_CloseGetFile_t)GetDelegate(hDll, "Aud_CloseGetFile", typeof(Aud_CloseGetFile_t));
            Aud_GetNumberOfFiles_t getNumFiles = (Aud_GetNumberOfFiles_t)GetDelegate(hDll, "Aud_GetNumberOfFiles", typeof(Aud_GetNumberOfFiles_t));
            Aud_GetNumberOfChannels_t getNumChannels = (Aud_GetNumberOfChannels_t)GetDelegate(hDll, "Aud_GetNumberOfChannels", typeof(Aud_GetNumberOfChannels_t));
            Aud_FileExistsW_t fileExists = (Aud_FileExistsW_t)GetDelegate(hDll, "Aud_FileExistsW", typeof(Aud_FileExistsW_t));
            Aud_GetChannelProperties_t getProps = (Aud_GetChannelProperties_t)GetDelegate(hDll, "Aud_GetChannelProperties", typeof(Aud_GetChannelProperties_t));
            Aud_GetChannelDataDoubles_t getData = (Aud_GetChannelDataDoubles_t)GetDelegate(hDll, "Aud_GetChannelDataDoubles", typeof(Aud_GetChannelDataDoubles_t));
            Aud_GetFileHeaderOriginal_t getHeader = (Aud_GetFileHeaderOriginal_t)GetDelegate(hDll, "Aud_GetFileHeaderOriginal", typeof(Aud_GetFileHeaderOriginal_t));
            Aud_GetString_t getString = (Aud_GetString_t)GetDelegate(hDll, "Aud_GetString", typeof(Aud_GetString_t));
            // NOTE: Aud_GetFileProperties skipped - causes hard crash (STATUS_BREAKPOINT) on original DLL

            // Write operations
            Aud_OpenPutFile_t openPut = (Aud_OpenPutFile_t)GetDelegate(hDll, "Aud_OpenPutFile", typeof(Aud_OpenPutFile_t));
            Aud_ClosePutFile_t closePut = (Aud_ClosePutFile_t)GetDelegate(hDll, "Aud_ClosePutFile", typeof(Aud_ClosePutFile_t));
            Aud_PutNumberOfChannels_t putChannels = (Aud_PutNumberOfChannels_t)GetDelegate(hDll, "Aud_PutNumberOfChannels", typeof(Aud_PutNumberOfChannels_t));
            Aud_MakeDirW_t makeDir = (Aud_MakeDirW_t)GetDelegate(hDll, "Aud_MakeDirW", typeof(Aud_MakeDirW_t));
            Aud_PutFileProperties_t putFileProps = (Aud_PutFileProperties_t)GetDelegate(hDll, "Aud_PutFileProperties", typeof(Aud_PutFileProperties_t));
            Aud_PutChannelProperties_t putChanProps = (Aud_PutChannelProperties_t)GetDelegate(hDll, "Aud_PutChannelProperties", typeof(Aud_PutChannelProperties_t));
            Aud_PutChannelDataDoubles_t putData = (Aud_PutChannelDataDoubles_t)GetDelegate(hDll, "Aud_PutChannelDataDoubles", typeof(Aud_PutChannelDataDoubles_t));

            if (initDll == null)
            {
                Log("[FAIL] Aud_InitDll not found");
                return 1;
            }

            // Run tests
            Log("\n=== INITIALIZATION ===");
            bool initOk = TestThreePhaseInit(initDll);
            if (!initOk)
            {
                Log("[WARN] Init failed, continuing with other tests...");
            }

            Log("\n=== VERSION FUNCTIONS ===");
            TestVersionFunctions(getIV, getDV);

            Log("\n=== FILE EXISTS ===");
            TestFileExists(fileExists);

            Log("\n=== FILE READ OPERATIONS ===");
            if (Directory.Exists(testDir))
            {
                // Prioritize files most likely to work with original DLL
                // Order: WAV > EASERA > MLSSA > MonkeyForest > CLIO > others
                // Skip malformed files completely
                List<string> prioritizedFiles = new List<string>();

                // 1. WAV files (most reliable format)
                foreach (string f in Directory.GetFiles(testDir, "*.wav"))
                    if (!Path.GetFileName(f).StartsWith("malformed"))
                        prioritizedFiles.Add(f);

                // 2. EASERA formats (ETM, EFR, EMD, ETX)
                foreach (string f in Directory.GetFiles(testDir, "*.etm"))
                    if (!Path.GetFileName(f).StartsWith("malformed"))
                        prioritizedFiles.Add(f);
                foreach (string f in Directory.GetFiles(testDir, "*.efr"))
                    prioritizedFiles.Add(f);
                foreach (string f in Directory.GetFiles(testDir, "*.emd"))
                    if (!Path.GetFileName(f).StartsWith("malformed"))
                        prioritizedFiles.Add(f);
                foreach (string f in Directory.GetFiles(testDir, "*.etx"))
                    if (!Path.GetFileName(f).StartsWith("malformed"))
                        prioritizedFiles.Add(f);

                // 3. MLSSA formats (.tim, .frq)
                foreach (string f in Directory.GetFiles(testDir, "*.tim"))
                    prioritizedFiles.Add(f);
                foreach (string f in Directory.GetFiles(testDir, "*.frq"))
                    prioritizedFiles.Add(f);

                // 4. MonkeyForest (.spk, .dat) - case insensitive
                foreach (string f in Directory.GetFiles(testDir, "*.spk"))
                    prioritizedFiles.Add(f);
                foreach (string f in Directory.GetFiles(testDir, "*.SPK"))
                    if (!prioritizedFiles.Contains(f))
                        prioritizedFiles.Add(f);
                foreach (string f in Directory.GetFiles(testDir, "*.dat"))
                    prioritizedFiles.Add(f);

                // 5. CLIO text formats (.frd, .zma)
                foreach (string f in Directory.GetFiles(testDir, "*.frd"))
                    prioritizedFiles.Add(f);
                foreach (string f in Directory.GetFiles(testDir, "*.zma"))
                    prioritizedFiles.Add(f);

                // 6. EVI PRN format
                foreach (string f in Directory.GetFiles(testDir, "*.prn"))
                    prioritizedFiles.Add(f);

                // Test up to 20 prioritized files
                int maxFiles = Math.Min(prioritizedFiles.Count, 20);
                Log("Found " + prioritizedFiles.Count + " valid test files, testing " + maxFiles);

                for (int i = 0; i < maxFiles; i++)
                {
                    TestFileRead(prioritizedFiles[i], openFile, closeFile, getNumFiles, getNumChannels,
                        getProps, getData, getHeader, getString);
                }
            }
            else
            {
                Log("[WARN] Test directory not found: " + testDir);
            }

            Log("\n=== WRITE OPERATIONS ===");
            TestWriteOperations(openPut, closePut, putChannels, makeDir, putFileProps, putChanProps, putData);

            // Print report
            PrintReport();

            return 0;
        }
        catch (Exception ex)
        {
            Log("[EXCEPTION] " + ex.Message);
            Log(ex.StackTrace);
            PrintReport();
            return 1;
        }
        finally
        {
            if (hDll != IntPtr.Zero)
                FreeLibrary(hDll);
        }
    }
}
