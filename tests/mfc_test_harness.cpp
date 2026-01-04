/*
 * MFC Test Harness for target.dll
 *
 * The original DLL uses MFC internally and returns -28 when called from
 * non-MFC applications. This harness provides proper MFC initialization.
 *
 * Compile with Visual Studio:
 *   cl /EHsc /MD mfc_test_harness.cpp /link /SUBSYSTEM:CONSOLE
 *
 * Or use MSBuild with the provided .vcxproj
 */

#include <windows.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

// DLL function signatures
typedef double (__cdecl *Aud_GetInterfaceVersion_t)(void);
typedef double (__cdecl *Aud_GetDllVersion_t)(void);
typedef unsigned int (__cdecl *Aud_InitDll_t)(unsigned int magic);
typedef int (__cdecl *Aud_OpenGetFile_t)(int format, const wchar_t* path, const wchar_t* hint);
typedef int (__cdecl *Aud_GetNumberOfFiles_t)(unsigned int* out_count);
typedef int (__cdecl *Aud_GetNumberOfChannels_t)(unsigned int file_idx, unsigned int* out_count);
typedef int (__cdecl *Aud_CloseGetFile_t)(void);

#define AUD_MAGIC 0x42754C2E

void print_json_result(const char* dll_name, const char* test_file,
                       int open_ret, int num_files, int num_channels) {
    printf("{\n");
    printf("  \"dll\": \"%s\",\n", dll_name);
    printf("  \"file\": \"%s\",\n", test_file);
    printf("  \"open_ret\": %d,\n", open_ret);
    printf("  \"num_files\": %d,\n", num_files);
    printf("  \"num_channels\": %d\n", num_channels);
    printf("}\n");
}

int test_dll(const char* dll_path, const char* dll_name, const wchar_t* test_file, const char* test_file_name) {
    HMODULE hDll = LoadLibraryA(dll_path);
    if (!hDll) {
        fprintf(stderr, "Failed to load DLL: %s (error %lu)\n", dll_path, GetLastError());
        return 1;
    }

    // Get function pointers
    Aud_GetInterfaceVersion_t Aud_GetInterfaceVersion =
        (Aud_GetInterfaceVersion_t)GetProcAddress(hDll, "Aud_GetInterfaceVersion");
    Aud_GetDllVersion_t Aud_GetDllVersion =
        (Aud_GetDllVersion_t)GetProcAddress(hDll, "Aud_GetDllVersion");
    Aud_InitDll_t Aud_InitDll =
        (Aud_InitDll_t)GetProcAddress(hDll, "Aud_InitDll");
    Aud_OpenGetFile_t Aud_OpenGetFile =
        (Aud_OpenGetFile_t)GetProcAddress(hDll, "Aud_OpenGetFile");
    Aud_GetNumberOfFiles_t Aud_GetNumberOfFiles =
        (Aud_GetNumberOfFiles_t)GetProcAddress(hDll, "Aud_GetNumberOfFiles");
    Aud_GetNumberOfChannels_t Aud_GetNumberOfChannels =
        (Aud_GetNumberOfChannels_t)GetProcAddress(hDll, "Aud_GetNumberOfChannels");
    Aud_CloseGetFile_t Aud_CloseGetFile =
        (Aud_CloseGetFile_t)GetProcAddress(hDll, "Aud_CloseGetFile");

    if (!Aud_InitDll || !Aud_OpenGetFile) {
        fprintf(stderr, "Failed to get function pointers from %s\n", dll_path);
        FreeLibrary(hDll);
        return 1;
    }

    // Initialize
    unsigned int session_magic = Aud_InitDll(AUD_MAGIC);
    if (session_magic == 0) {
        fprintf(stderr, "Aud_InitDll failed for %s\n", dll_name);
        FreeLibrary(hDll);
        return 1;
    }

    // Open file
    int open_ret = Aud_OpenGetFile(0, test_file, L"");

    int num_files = -1;
    int num_channels = -1;

    if (open_ret == 0) {
        unsigned int files_count = 0;
        Aud_GetNumberOfFiles(&files_count);
        num_files = (int)files_count;

        unsigned int channels_count = 0;
        Aud_GetNumberOfChannels(0, &channels_count);
        num_channels = (int)channels_count;

        Aud_CloseGetFile();
    }

    print_json_result(dll_name, test_file_name, open_ret, num_files, num_channels);

    FreeLibrary(hDll);
    return 0;
}

int main(int argc, char* argv[]) {
    if (argc < 4) {
        fprintf(stderr, "Usage: %s <original_dll> <rebuilt_dll> <test_file>\n", argv[0]);
        return 1;
    }

    const char* original_dll = argv[1];
    const char* rebuilt_dll = argv[2];
    const char* test_file = argv[3];

    // Convert test file path to wide string
    int len = MultiByteToWideChar(CP_UTF8, 0, test_file, -1, NULL, 0);
    wchar_t* test_file_w = (wchar_t*)malloc(len * sizeof(wchar_t));
    MultiByteToWideChar(CP_UTF8, 0, test_file, -1, test_file_w, len);

    printf("[\n");

    // Test original DLL
    test_dll(original_dll, "original", test_file_w, test_file);

    printf(",\n");

    // Test rebuilt DLL
    test_dll(rebuilt_dll, "rebuilt", test_file_w, test_file);

    printf("]\n");

    free(test_file_w);
    return 0;
}
