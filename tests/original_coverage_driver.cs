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
    delegate int Aud_GetFileProperties_t(uint fileIdx, [Out] byte[] buffer);

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

    static int GetFormatCode(string path)
    {
        string ext = Path.GetExtension(path).ToLower();
        if (ext == ".etm") return 1;
        if (ext == ".efr") return 2;
        if (ext == ".emd") return 3;
        if (ext == ".etx") return 5;
        if (ext == ".wav") return 9;
        if (ext == ".tim") return 10;
        if (ext == ".frq") return 11;
        if (ext == ".dat") return 12;
        if (ext == ".spk") return 13;
        if (ext == ".frd") return 24;
        if (ext == ".zma") return 24;
        if (ext == ".txt") return 19;
        if (ext == ".prn") return 36;
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
        Aud_GetFileHeaderOriginal_t getHeader, Aud_GetString_t getString,
        Aud_GetFileProperties_t getFileProps)
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

        // GetFileProperties
        if (getFileProps != null)
        {
            TotalCalls++;
            try
            {
                byte[] filePropBuf = new byte[560];
                int fpRet = getFileProps(0, filePropBuf);
                MarkCovered("Aud_GetFileProperties");
                Log("  FileProperties: " + fpRet);
                SuccessfulCalls++;
            }
            catch (Exception ex)
            {
                Log("  FileProperties: EXCEPTION - " + ex.Message);
            }
        }

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
        Console.WriteLine("\n" + new string('=', 60));
        Console.WriteLine("ORIGINAL DLL COVERAGE REPORT");
        Console.WriteLine(new string('=', 60));

        int covered = 0;
        foreach (KeyValuePair<string, bool> kvp in FunctionsCovered)
        {
            if (kvp.Value) covered++;
            Console.WriteLine("  " + (kvp.Value ? "[OK]" : "[  ]") + " " + kvp.Key);
        }

        Console.WriteLine("\n" + new string('-', 60));
        Console.WriteLine("Functions covered: " + covered + "/" + FunctionsCovered.Count);
        Console.WriteLine("Total DLL calls: " + TotalCalls);
        Console.WriteLine("Successful calls: " + SuccessfulCalls);
        Console.WriteLine("Success rate: " + (100.0 * SuccessfulCalls / Math.Max(TotalCalls, 1)).ToString("F1") + "%");
        Console.WriteLine(new string('=', 60));
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
            "Aud_GetFileProperties", "Aud_GetChannelProperties",
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
            Aud_GetFileProperties_t getFileProps = (Aud_GetFileProperties_t)GetDelegate(hDll, "Aud_GetFileProperties", typeof(Aud_GetFileProperties_t));

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
                string[] testFiles = Directory.GetFiles(testDir);
                // Only test first 10 files to avoid timeout
                int maxFiles = Math.Min(testFiles.Length, 10);
                for (int i = 0; i < maxFiles; i++)
                {
                    TestFileRead(testFiles[i], openFile, closeFile, getNumFiles, getNumChannels,
                        getProps, getData, getHeader, getString, getFileProps);
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
