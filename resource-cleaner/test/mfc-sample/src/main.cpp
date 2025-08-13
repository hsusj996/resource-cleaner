\
#include "resource.h"
#include <windows.h>

static INT_PTR CALLBACK DlgProc(HWND hDlg, UINT msg, WPARAM wParam, LPARAM lParam)
{
    switch (msg)
    {
    case WM_COMMAND:
        switch (LOWORD(wParam))
        {
        case IDM_ABOUT:
            DialogBoxW(GetModuleHandleW(NULL), MAKEINTRESOURCEW(IDD_ABOUTBOX), hDlg, DlgProc);
            return (INT_PTR)TRUE;
        case IDM_EXIT:
        case IDC_BTN_OK:
            EndDialog(hDlg, 0);
            return (INT_PTR)TRUE;
        default:
            break;
        }
        break;
    case WM_CLOSE:
        EndDialog(hDlg, 0);
        return (INT_PTR)TRUE;
    }
    return (INT_PTR)FALSE;
}

int APIENTRY wWinMain(HINSTANCE hInstance, HINSTANCE, LPWSTR, int)
{
    wchar_t title[128] = {0};
    LoadStringW(hInstance, IDS_APP_TITLE, title, 128); // IDS_APP_TITLE usage

    // Use accelerator table (IDR_ACCEL)
    HACCEL hAccel = LoadAcceleratorsW(hInstance, MAKEINTRESOURCEW(IDR_ACCEL));
    (void)hAccel;

    // Show main dialog (IDD_MAIN_DIALOG) which has IDC_STATIC_TEXT and IDC_BTN_OK
    DialogBoxW(hInstance, MAKEINTRESOURCEW(IDD_MAIN_DIALOG), NULL, DlgProc);
    return 0;
}
