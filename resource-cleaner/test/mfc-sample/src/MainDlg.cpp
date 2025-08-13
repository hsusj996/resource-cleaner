\
#include "MainDlg.h"
#include <windows.h>

void UseControls()
{
    // Explicit references so the cleaner finds tokens in code too
    int a = IDC_STATIC_TEXT;
    int b = IDC_BTN_OK;
    int c = IDM_EXIT;
    int d = IDM_ABOUT;
    (void)a; (void)b; (void)c; (void)d;
}
