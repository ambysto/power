// Ambysto Windows Installer — Custom Dark-Mode 5-Screen MsiEmbeddedUI
// Follows the 5-screen flow (Welcome -> Progress+Showcase -> Offer -> Newsletter -> Finish)
// styled in Ambysto's "The Printed Readout" design language (DESIGN.md) with Smart Sibling Filtering.

#define _CRT_SECURE_NO_WARNINGS
#ifndef UNICODE
#define UNICODE
#endif
#ifndef _UNICODE
#define _UNICODE
#endif
#define WIN32_LEAN_AND_MEAN

#include <windows.h>
#include <windowsx.h>
#include <objidl.h>
#include <gdiplus.h>
#include <msi.h>
#include <msiquery.h>
#include <shellapi.h>
#include <urlmon.h>
#include <stdio.h>
#include <wchar.h>
#include <string>
#include <vector>

#pragma comment(linker, "/EXPORT:InitializeEmbeddedUI=InitializeEmbeddedUI")
#pragma comment(linker, "/EXPORT:EmbeddedUIHandler=EmbeddedUIHandler")
#pragma comment(linker, "/EXPORT:ShutdownEmbeddedUI=ShutdownEmbeddedUI")

#ifndef AMBYSTO_APP_KEY
#define AMBYSTO_APP_KEY L"power"
#endif
#ifndef AMBYSTO_APP_NAME
#define AMBYSTO_APP_NAME L"Ambysto Power"
#endif
#ifndef AMBYSTO_APP_EXE
#define AMBYSTO_APP_EXE L"AmbystoPower.exe"
#endif
#ifndef AMBYSTO_APP_URL
#define AMBYSTO_APP_URL L"https://power.ambysto.com"
#endif
#ifndef AMBYSTO_TONE_R
#define AMBYSTO_TONE_R 0x9F
#endif
#ifndef AMBYSTO_TONE_G
#define AMBYSTO_TONE_G 0xC6
#endif
#ifndef AMBYSTO_TONE_B
#define AMBYSTO_TONE_B 0x75
#endif

using namespace Gdiplus;

struct SiblingInfo {
    const wchar_t* key;
    const wchar_t* fullName;
    const wchar_t* regSubKey;
    const wchar_t* installSubDir;
    const wchar_t* exeName;
    const wchar_t* upgradeCode;
    const wchar_t* downloadUrl;
    const wchar_t* homeUrl;
};

static const SiblingInfo kAllApps[] = {
    { L"lean",    L"Ambysto Lean",    L"Software\\Ambysto\\Lean",    L"Programs\\Ambysto Lean",    L"AmbystoLean.exe",    L"{A0E24189-731D-4A00-A31C-D32E78A09472}", L"https://github.com/ambysto/ambysto-downloads/releases/download/windows-v0.1.1/AmbystoLean.msi",    L"https://lean.ambysto.com" },
    { L"optimer", L"Ambysto Optimer", L"Software\\Ambysto\\Optimer", L"Programs\\Ambysto Optimer", L"AmbystoOptimer.exe", L"{B7E84F32-5A19-4C81-9E42-8F03A1B95C14}", L"https://github.com/ambysto/ambysto-downloads/releases/download/windows-v0.1.1/AmbystoOptimer.msi", L"https://optimer.ambysto.com" },
    { L"devbox",  L"Ambysto DevBox",  L"Software\\Ambysto\\DevBox",  L"Programs\\Ambysto DevBox",  L"AmbystoDevBox.exe",  L"{A6D91E43-7B2C-4F18-8E51-9C04B2D73E19}", L"https://github.com/ambysto/ambysto-downloads/releases/download/windows-v0.1.1/AmbystoDevBox.msi",  L"https://devbox.ambysto.com" },
    { L"power",   L"Ambysto Power",   L"Software\\Ambysto\\Power",   L"Programs\\Ambysto Power",   L"AmbystoPower.exe",   L"{4D8C92F1-6E34-4B73-8A19-7C52E10F9B65}", L"https://github.com/ambysto/ambysto-downloads/releases/download/windows-v0.1.1/AmbystoPower.msi",   L"https://power.ambysto.com" },
    { L"tune",    L"Ambysto Tune",    L"Software\\Ambysto\\Tune",    L"Programs\\Ambysto Tune",    L"AmbystoTune.exe",    L"{7E6D5A18-9F3C-44D2-B521-48C0E7D28B3A}", L"https://github.com/ambysto/ambysto-downloads/releases/download/windows-v0.1.1/AmbystoTune.msi",    L"https://tune.ambysto.com" },
    { L"gate",    L"Ambysto Gate",    L"Software\\Ambysto\\Gate",    L"Programs\\Ambysto Gate",    L"AmbystoGate.exe",    L"{8F2D63E1-9C4B-4E17-A28D-5C71B904E621}", L"https://github.com/ambysto/ambysto-downloads/releases/download/windows-v0.1.1/AmbystoGate.msi",    L"https://gate.ambysto.com" },
};

enum InstallerStage {
    STAGE_WELCOME = 1,
    STAGE_PROGRESS = 2,
    STAGE_OFFER = 3,
    STAGE_NEWSLETTER = 4,
    STAGE_FINISH = 5
};

static const int kWinW = 960;
static const int kWinH = 660;
static const UINT WM_AMBYSTO_PROGRESS = WM_APP + 10;
static const UINT WM_AMBYSTO_INSTALL_DONE = WM_APP + 11;
static const UINT_PTR TIMER_PROGRESS = 1;
static const UINT_PTR TIMER_AUTOTEST = 2;

static HINSTANCE g_hInst = NULL;
static HWND g_hwnd = NULL;
static HANDLE g_hUiThread = NULL;
static HANDLE g_hStartInstallEvent = NULL;
static HANDLE g_hUiFinishedEvent = NULL;
static ULONG_PTR g_gdiplusToken = 0;

static std::wstring g_resourceDir;
static std::wstring g_autotestDir;
static bool g_autotestMode = false;

static volatile LONG g_userCancelled = 0;
static volatile LONG g_targetProgress = 5;
static int g_displayProgress = 5;
static bool g_msiInstallCompleted = false;

static InstallerStage g_stage = STAGE_WELCOME;
static std::vector<SiblingInfo> g_candidates;
static int g_offer1Idx = -1;
static bool g_offer1Checked = true;
static bool g_offer1AcceptedOnScreen2 = false;
static bool g_offer1DownloadTriggered = false;
static int g_offer3Idx = -1;
static bool g_offer3Accepted = false;
static bool g_offer3DownloadTriggered = false;
static std::vector<std::wstring> g_triggeredDownloads;

static std::wstring g_emailText;
static bool g_emailFocus = true;
static bool g_learnMoreChecked = false;
static ULONGLONG g_stageEnteredTick = 0;
static std::vector<int> g_visitedStages;

static Bitmap* g_backBuffer = nullptr;

static bool TokenListContains(const std::wstring& csv, const wchar_t* token) {
    std::wstring needle = token;
    size_t pos = 0;
    while (pos <= csv.size()) {
        size_t comma = csv.find(L',', pos);
        if (comma == std::wstring::npos) comma = csv.size();
        std::wstring item = csv.substr(pos, comma - pos);
        while (!item.empty() && (item.front() == L' ' || item.front() == L'\t')) item.erase(item.begin());
        while (!item.empty() && (item.back() == L' ' || item.back() == L'\t')) item.pop_back();
        if (_wcsicmp(item.c_str(), needle.c_str()) == 0) return true;
        pos = comma + 1;
    }
    return false;
}

static bool IsSiblingInstalled(const SiblingInfo& app) {
    wchar_t envBuf[512] = {0};
    DWORD envLen = GetEnvironmentVariableW(L"AMBYSTO_INSTALLER_INSTALLED_APPS", envBuf, 511);
    if (envLen > 0 && envLen < 511) {
        if (_wcsicmp(envBuf, L"NONE") == 0) return false;
        if (_wcsicmp(envBuf, L"ALL") == 0) return true;
        return TokenListContains(envBuf, app.key);
    }

    // 1. Check %LOCALAPPDATA%\Programs\Ambysto <App>\Ambysto<App>.exe
    wchar_t localAppData[MAX_PATH] = {0};
    if (GetEnvironmentVariableW(L"LOCALAPPDATA", localAppData, MAX_PATH) > 0) {
        std::wstring exePath = std::wstring(localAppData) + L"\\" + app.installSubDir + L"\\" + app.exeName;
        DWORD attr = GetFileAttributesW(exePath.c_str());
        if (attr != INVALID_FILE_ATTRIBUTES && !(attr & FILE_ATTRIBUTE_DIRECTORY)) {
            return true;
        }
    }

    // 2. Check HKCU\Software\Ambysto\<App> StartMenuShortcut / Installed
    HKEY hKey = NULL;
    if (RegOpenKeyExW(HKEY_CURRENT_USER, app.regSubKey, 0, KEY_READ, &hKey) == ERROR_SUCCESS) {
        DWORD val = 0, sz = sizeof(val), type = 0;
        bool hasMarker = (RegQueryValueExW(hKey, L"StartMenuShortcut", NULL, &type, (LPBYTE)&val, &sz) == ERROR_SUCCESS && val == 1);
        if (!hasMarker) {
            sz = sizeof(val);
            hasMarker = (RegQueryValueExW(hKey, L"Installed", NULL, &type, (LPBYTE)&val, &sz) == ERROR_SUCCESS && val == 1);
        }
        RegCloseKey(hKey);
        if (hasMarker) return true;
    }

    // 3. Check MSI UpgradeCode via MsiEnumRelatedProductsW
    wchar_t productBuf[39] = {0};
    if (MsiEnumRelatedProductsW(app.upgradeCode, 0, 0, productBuf) == ERROR_SUCCESS) {
        return true;
    }

    return false;
}

static void DetectUninstalledSiblings() {
    g_candidates.clear();
    g_triggeredDownloads.clear();
    g_offer1DownloadTriggered = false;
    g_offer3DownloadTriggered = false;
    g_offer3Accepted = false;

    for (size_t i = 0; i < sizeof(kAllApps) / sizeof(kAllApps[0]); ++i) {
        if (_wcsicmp(kAllApps[i].key, AMBYSTO_APP_KEY) == 0) continue;
        if (!IsSiblingInstalled(kAllApps[i])) {
            g_candidates.push_back(kAllApps[i]);
        }
    }

    if (g_candidates.size() >= 1) {
        g_offer1Idx = 0;
        g_offer1Checked = true;
        g_offer1AcceptedOnScreen2 = false;
    } else {
        g_offer1Idx = -1;
        g_offer1Checked = false;
        g_offer1AcceptedOnScreen2 = false;
    }

    if (g_candidates.size() >= 2) {
        g_offer3Idx = 1;
    } else {
        g_offer3Idx = -1;
    }
}

static Bitmap* LoadArtPng(const std::wstring& filename) {
    std::wstring fullPath = g_resourceDir + L"\\" + filename;
    Bitmap* bmp = Bitmap::FromFile(fullPath.c_str(), FALSE);
    if (bmp && bmp->GetLastStatus() == Ok) {
        return bmp;
    }
    delete bmp;
    return nullptr;
}

static int GetEncoderClsid(const WCHAR* format, CLSID* pClsid) {
    UINT num = 0, size = 0;
    GetImageEncodersSize(&num, &size);
    if (size == 0) return -1;
    ImageCodecInfo* pCodecInfo = (ImageCodecInfo*)(malloc(size));
    if (!pCodecInfo) return -1;
    GetImageEncoders(num, size, pCodecInfo);
    for (UINT j = 0; j < num; ++j) {
        if (wcscmp(pCodecInfo[j].MimeType, format) == 0) {
            *pClsid = pCodecInfo[j].Clsid;
            free(pCodecInfo);
            return (int)j;
        }
    }
    free(pCodecInfo);
    return -1;
}

static void DumpAutotestScreen(const wchar_t* name) {
    if (!g_autotestMode || !g_backBuffer || g_autotestDir.empty()) return;
    CreateDirectoryW(g_autotestDir.c_str(), NULL);
    std::wstring path = g_autotestDir + L"\\" + name;
    CLSID pngClsid;
    if (GetEncoderClsid(L"image/png", &pngClsid) >= 0) {
        g_backBuffer->Save(path.c_str(), &pngClsid, NULL);
    }
}

static void WriteAutotestSummary() {
    if (!g_autotestMode || g_autotestDir.empty()) return;
    CreateDirectoryW(g_autotestDir.c_str(), NULL);
    std::wstring path = g_autotestDir + L"\\autotest-summary.txt";
    FILE* f = _wfopen(path.c_str(), L"w, ccs=UTF-8");
    if (!f) return;
    fwprintf(f, L"app_key=%ls\n", AMBYSTO_APP_KEY);
    fwprintf(f, L"uninstalled_siblings=");
    for (size_t i = 0; i < g_candidates.size(); ++i) {
        if (i > 0) fwprintf(f, L",");
        fwprintf(f, L"%ls", g_candidates[i].key);
    }
    fwprintf(f, L"\n");
    fwprintf(f, L"screen1_offer=%ls\n", (g_offer1Idx >= 0) ? g_candidates[g_offer1Idx].key : L"none");
    fwprintf(f, L"screen2_offer=%ls\n", (g_offer1Idx >= 0) ? g_candidates[g_offer1Idx].key : L"none");
    fwprintf(f, L"screen3_offer=%ls\n", (g_offer3Idx >= 0) ? g_candidates[g_offer3Idx].key : L"none");
    fwprintf(f, L"visited_stages=");
    for (size_t i = 0; i < g_visitedStages.size(); ++i) {
        if (i > 0) fwprintf(f, L",");
        fwprintf(f, L"%d", g_visitedStages[i]);
    }
    fwprintf(f, L"\n");
    fwprintf(f, L"triggered_downloads=");
    for (size_t i = 0; i < g_triggeredDownloads.size(); ++i) {
        if (i > 0) fwprintf(f, L",");
        fwprintf(f, L"%ls", g_triggeredDownloads[i].c_str());
    }
    fwprintf(f, L"\n");
    fclose(f);
}

static void ComposeCurrentScreen() {
    if (!g_backBuffer) {
        g_backBuffer = new Bitmap(kWinW, kWinH, PixelFormat32bppARGB);
    }
    Graphics g(g_backBuffer);
    g.SetInterpolationMode(InterpolationModeNearestNeighbor);
    g.SetPixelOffsetMode(PixelOffsetModeHalf);
    g.SetSmoothingMode(SmoothingModeAntiAlias);
    g.SetTextRenderingHint(TextRenderingHintClearTypeGridFit);

    if (g_stage == STAGE_WELCOME) {
        Bitmap* base = LoadArtPng(L"Screen1_Base.png");
        if (base) {
            g.DrawImage(base, 0, 0, kWinW, kWinH);
            delete base;
        }
        if (g_offer1Idx >= 0 && g_offer1Idx < (int)g_candidates.size()) {
            std::wstring barName = std::wstring(L"OfferBar_") + g_candidates[g_offer1Idx].key + (g_offer1Checked ? L"_on.png" : L"_off.png");
            Bitmap* bar = LoadArtPng(barName);
            if (bar) {
                g.DrawImage(bar, 1, 536, 958, 122);
                delete bar;
            }
        }
    } else if (g_stage == STAGE_PROGRESS) {
        if (g_offer1Idx >= 0 && g_offer1Idx < (int)g_candidates.size()) {
            Bitmap* base = LoadArtPng(g_offer1AcceptedOnScreen2 ? L"Screen2_Accepted.png" : L"Screen2_Base.png");
            if (base) {
                g.DrawImage(base, 0, 0, kWinW, kWinH);
                delete base;
            }
            std::wstring showName = std::wstring(L"Showcase_") + g_candidates[g_offer1Idx].key + L".png";
            Bitmap* show = LoadArtPng(showName);
            if (show) {
                g.DrawImage(show, 38, 110, 884, 368);
                delete show;
            }
        } else {
            Bitmap* noOffer = LoadArtPng(L"Screen2_NoOffer.png");
            if (noOffer) {
                g.DrawImage(noOffer, 0, 0, kWinW, kWinH);
                delete noOffer;
            }
        }

        // Draw dynamic progress text and tone bar
        g.SetTextRenderingHint(TextRenderingHintClearTypeGridFit);
        wchar_t progText[64] = {0};
        swprintf(progText, 63, L"Installing.... %d%%", g_displayProgress);
        Font monoFont(L"Consolas", 10.5f, FontStyleBold, UnitPoint);
        SolidBrush muteBrush(Color(255, 0xC6, 0xC6, 0xC2));
        g.DrawString(progText, -1, &monoFont, PointF(36.0f, 62.0f), &muteBrush);

        int barW = (int)((kWinW - 2) * (g_displayProgress / 100.0));
        if (barW < 6) barW = 6;
        if (barW > kWinW - 2) barW = kWinW - 2;
        SolidBrush toneBrush(Color(255, AMBYSTO_TONE_R, AMBYSTO_TONE_G, AMBYSTO_TONE_B));
        g.FillRectangle(&toneBrush, 1, 92, barW, 6);
    } else if (g_stage == STAGE_OFFER) {
        Bitmap* base = LoadArtPng(L"Screen3_Base.png");
        if (base) {
            g.DrawImage(base, 0, 0, kWinW, kWinH);
            delete base;
        }
        if (g_offer3Idx >= 0 && g_offer3Idx < (int)g_candidates.size()) {
            std::wstring showName = std::wstring(L"Showcase_") + g_candidates[g_offer3Idx].key + L".png";
            Bitmap* show = LoadArtPng(showName);
            if (show) {
                g.DrawImage(show, 38, 110, 884, 368);
                delete show;
            }
        }
    } else if (g_stage == STAGE_NEWSLETTER) {
        Bitmap* base = LoadArtPng(L"Screen4_Newsletter.png");
        if (base) {
            g.DrawImage(base, 0, 0, kWinW, kWinH);
            delete base;
        }
        g.SetTextRenderingHint(TextRenderingHintClearTypeGridFit);
        Font inputFont(L"Segoe UI", 10.5f, FontStyleRegular, UnitPoint);
        if (g_emailText.empty()) {
            SolidBrush placeholderBrush(Color(255, 0xB4, 0xB4, 0xB8));
            g.DrawString(L"Please enter your email address to get your privileges.", -1, &inputFont, PointF(130.0f, 525.0f), &placeholderBrush);
        } else {
            SolidBrush inkBrush(Color(255, 0xE8, 0xE8, 0xE6));
            std::wstring shown = g_emailText + (g_emailFocus ? L"|" : L"");
            g.DrawString(shown.c_str(), -1, &inputFont, PointF(130.0f, 525.0f), &inkBrush);
        }
    } else if (g_stage == STAGE_FINISH) {
        Bitmap* base = LoadArtPng(g_learnMoreChecked ? L"Screen5_Finish.png" : L"Screen5_Finish_off.png");
        if (base) {
            g.DrawImage(base, 0, 0, kWinW, kWinH);
            delete base;
        }
    }
}

static void EnterStage(InstallerStage nextStage) {
    g_stage = nextStage;
    g_stageEnteredTick = GetTickCount64();
    g_visitedStages.push_back((int)nextStage);
    ComposeCurrentScreen();
    if (g_hwnd) {
        InvalidateRect(g_hwnd, NULL, FALSE);
        UpdateWindow(g_hwnd);
    }

    if (g_autotestMode) {
        switch (nextStage) {
            case STAGE_WELCOME:    DumpAutotestScreen(L"screen-1-welcome.png"); break;
            case STAGE_PROGRESS:   DumpAutotestScreen(L"screen-2-progress.png"); break;
            case STAGE_OFFER:      DumpAutotestScreen(L"screen-3-offer.png"); break;
            case STAGE_NEWSLETTER: DumpAutotestScreen(L"screen-4-newsletter.png"); break;
            case STAGE_FINISH:     DumpAutotestScreen(L"screen-5-finish.png"); WriteAutotestSummary(); break;
        }
    }
}

static void AdvanceAfterProgressComplete() {
    // Decide if Screen 3 (Post-Install Offer) has an unaccepted sibling app to present
    if (g_offer3Idx < 0 && g_offer1Idx >= 0 && !g_offer1Checked && !g_offer1AcceptedOnScreen2) {
        g_offer3Idx = g_offer1Idx;
    }
    if (g_offer3Idx >= 0 && g_offer3Idx < (int)g_candidates.size()) {
        EnterStage(STAGE_OFFER);
    } else {
        EnterStage(STAGE_NEWSLETTER);
    }
}

static void TriggerSiblingDownload(const SiblingInfo& sib) {
    g_triggeredDownloads.push_back(sib.downloadUrl);
    wchar_t noLaunch[32] = {0};
    if (GetEnvironmentVariableW(L"AMBYSTO_INSTALLER_NO_LAUNCH", noLaunch, 31) > 0 && wcscmp(noLaunch, L"1") == 0) {
        return;
    }
    ShellExecuteW(NULL, L"open", sib.downloadUrl, NULL, NULL, SW_SHOWNORMAL);
}

static void PerformFinalActionsAndClose(HWND hwnd) {
    wchar_t noLaunch[32] = {0};
    bool suppressLaunch = (GetEnvironmentVariableW(L"AMBYSTO_INSTALLER_NO_LAUNCH", noLaunch, 31) > 0 && wcscmp(noLaunch, L"1") == 0);

    if (g_offer1Idx >= 0 && g_offer1Idx < (int)g_candidates.size() &&
        (g_offer1Checked || g_offer1AcceptedOnScreen2) && !g_offer1DownloadTriggered && !g_offer3DownloadTriggered) {
        g_offer1DownloadTriggered = true;
        TriggerSiblingDownload(g_candidates[g_offer1Idx]);
    }
    if (g_offer3Idx >= 0 && g_offer3Idx < (int)g_candidates.size() &&
        g_offer3Accepted && !g_offer3DownloadTriggered) {
        g_offer3DownloadTriggered = true;
        TriggerSiblingDownload(g_candidates[g_offer3Idx]);
    }

    if (!suppressLaunch) {
        if (g_learnMoreChecked) {
            ShellExecuteW(NULL, L"open", AMBYSTO_APP_URL, NULL, NULL, SW_SHOWNORMAL);
        }

        // Launch the installed app
        wchar_t localAppData[MAX_PATH] = {0};
        if (GetEnvironmentVariableW(L"LOCALAPPDATA", localAppData, MAX_PATH) > 0) {
            std::wstring installDir = std::wstring(localAppData) + L"\\Programs\\" + AMBYSTO_APP_NAME;
            std::wstring exePath = installDir + L"\\" + AMBYSTO_APP_EXE;
            if (GetFileAttributesW(exePath.c_str()) != INVALID_FILE_ATTRIBUTES) {
                ShellExecuteW(NULL, L"open", exePath.c_str(), NULL, installDir.c_str(), SW_SHOWNORMAL);
            }
        }
    }

    if (g_autotestMode) {
        WriteAutotestSummary();
    }
    DestroyWindow(hwnd);
}

static bool PtInBox(int x, int y, int bx, int by, int bw, int bh) {
    return x >= bx && x <= (bx + bw) && y >= by && y <= (by + bh);
}

static LRESULT CALLBACK InstallerWndProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam) {
    switch (msg) {
        case WM_CREATE:
            SetTimer(hwnd, TIMER_PROGRESS, 60, NULL);
            if (g_autotestMode) {
                SetTimer(hwnd, TIMER_AUTOTEST, 450, NULL);
            }
            return 0;

        case WM_NCHITTEST: {
            POINT pt = { GET_X_LPARAM(lParam), GET_Y_LPARAM(lParam) };
            ScreenToClient(hwnd, &pt);
            if (pt.y >= 0 && pt.y < 48 && pt.x < 880) {
                return HTCAPTION;
            }
            return HTCLIENT;
        }

        case WM_PAINT: {
            PAINTSTRUCT ps;
            HDC hdc = BeginPaint(hwnd, &ps);
            if (g_backBuffer) {
                Graphics gScreen(hdc);
                gScreen.SetInterpolationMode(InterpolationModeNearestNeighbor);
                gScreen.SetPixelOffsetMode(PixelOffsetModeHalf);
                gScreen.DrawImage(g_backBuffer, 0, 0, kWinW, kWinH);
            }
            EndPaint(hwnd, &ps);
            return 0;
        }

        case WM_ERASEBKGND:
            return 1;

        case WM_TIMER: {
            if (wParam == TIMER_PROGRESS && g_stage == STAGE_PROGRESS) {
                LONG tgt = InterlockedCompareExchange(&g_targetProgress, 0, 0);
                if (g_msiInstallCompleted) tgt = 100;
                if (g_displayProgress < tgt) {
                    int step = 3;
                    if (g_autotestMode || g_offer1Idx < 0 || g_offer1AcceptedOnScreen2) {
                        step = (tgt - g_displayProgress) / 3;
                        if (step < 4) step = 4;
                        if (g_msiInstallCompleted && step < 14) step = 14;
                    }
                    g_displayProgress += step;
                    if (g_displayProgress > tgt) g_displayProgress = tgt;
                    ComposeCurrentScreen();
                    InvalidateRect(hwnd, NULL, FALSE);
                    if (g_autotestMode && g_displayProgress >= 35 && g_displayProgress <= 65) {
                        DumpAutotestScreen(L"screen-2-progress.png");
                    }
                }
                if (g_msiInstallCompleted && g_displayProgress >= 100) {
                    AdvanceAfterProgressComplete();
                }
            } else if (wParam == TIMER_AUTOTEST && g_autotestMode) {
                wchar_t autoAccept[16] = {0};
                bool acceptOffers = (GetEnvironmentVariableW(L"AMBYSTO_INSTALLER_AUTOTEST_ACCEPT", autoAccept, 15) > 0 && wcscmp(autoAccept, L"1") == 0);
                if (g_stage == STAGE_WELCOME) {
                    EnterStage(STAGE_PROGRESS);
                    SetEvent(g_hStartInstallEvent);
                } else if (g_stage == STAGE_PROGRESS && acceptOffers && g_offer1Idx >= 0 && g_offer1Idx < (int)g_candidates.size() && !g_offer1AcceptedOnScreen2) {
                    g_offer1AcceptedOnScreen2 = true;
                    g_offer1Checked = true;
                    if (!g_offer1DownloadTriggered) {
                        g_offer1DownloadTriggered = true;
                        TriggerSiblingDownload(g_candidates[g_offer1Idx]);
                    }
                    ComposeCurrentScreen();
                    InvalidateRect(hwnd, NULL, FALSE);
                } else if (g_stage == STAGE_OFFER) {
                    if (acceptOffers && g_offer3Idx >= 0 && g_offer3Idx < (int)g_candidates.size()) {
                        g_offer3Accepted = true;
                        if (!g_offer3DownloadTriggered) {
                            g_offer3DownloadTriggered = true;
                            TriggerSiblingDownload(g_candidates[g_offer3Idx]);
                        }
                    } else {
                        g_offer3Accepted = false;
                    }
                    EnterStage(STAGE_NEWSLETTER);
                } else if (g_stage == STAGE_NEWSLETTER) {
                    EnterStage(STAGE_FINISH);
                } else if (g_stage == STAGE_FINISH) {
                    KillTimer(hwnd, TIMER_AUTOTEST);
                    PerformFinalActionsAndClose(hwnd);
                }
            }
            return 0;
        }

        case WM_AMBYSTO_INSTALL_DONE: {
            g_msiInstallCompleted = true;
            InterlockedExchange(&g_targetProgress, 100);
            return 0;
        }

        case WM_LBUTTONDOWN: {
            int x = GET_X_LPARAM(lParam);
            int y = GET_Y_LPARAM(lParam);

            // Top right Minimize (—) and Close (×)
            if (PtInBox(x, y, 884, 8, 30, 32)) {
                ShowWindow(hwnd, SW_MINIMIZE);
                return 0;
            }
            if (PtInBox(x, y, 916, 8, 36, 32)) {
                if (g_stage == STAGE_WELCOME || g_stage == STAGE_PROGRESS) {
                    InterlockedExchange(&g_userCancelled, 1);
                    SetEvent(g_hStartInstallEvent);
                }
                DestroyWindow(hwnd);
                return 0;
            }

            if (g_stage == STAGE_WELCOME) {
                // Install button (356, 402, 248, 52)
                if (PtInBox(x, y, 356, 402, 248, 52)) {
                    EnterStage(STAGE_PROGRESS);
                    SetEvent(g_hStartInstallEvent);
                    return 0;
                }
                // Bottom sibling checkbox (32, 574, 580, 28)
                if (g_offer1Idx >= 0 && PtInBox(x, y, 32, 574, 580, 28)) {
                    g_offer1Checked = !g_offer1Checked;
                    ComposeCurrentScreen();
                    InvalidateRect(hwnd, NULL, FALSE);
                    return 0;
                }
                // Center EULA / Privacy Policy
                if (PtInBox(x, y, 530, 348, 175, 24)) {
                    ShellExecuteW(NULL, L"open", AMBYSTO_APP_URL, NULL, NULL, SW_SHOWNORMAL);
                    return 0;
                }
            } else if (g_stage == STAGE_PROGRESS) {
                // Accept and Install button (352, 536, 256, 48)
                if (g_offer1Idx >= 0 && g_offer1Idx < (int)g_candidates.size() && !g_offer1AcceptedOnScreen2 && PtInBox(x, y, 352, 536, 256, 48)) {
                    g_offer1AcceptedOnScreen2 = true;
                    g_offer1Checked = true;
                    if (!g_offer1DownloadTriggered) {
                        g_offer1DownloadTriggered = true;
                        TriggerSiblingDownload(g_candidates[g_offer1Idx]);
                    }
                    ComposeCurrentScreen();
                    InvalidateRect(hwnd, NULL, FALSE);
                    return 0;
                }
            } else if (g_stage == STAGE_OFFER) {
                // If user clicked Screen 2's centered "Accept and Install" (352, 536, 256, 48) right as Screen 2 transitioned to Screen 3:
                if (GetTickCount64() - g_stageEnteredTick < 650 && PtInBox(x, y, 352, 536, 256, 48)) {
                    if (g_offer1Idx >= 0 && g_offer1Idx < (int)g_candidates.size() && !g_offer1DownloadTriggered) {
                        g_offer1AcceptedOnScreen2 = true;
                        g_offer1Checked = true;
                        g_offer1DownloadTriggered = true;
                        TriggerSiblingDownload(g_candidates[g_offer1Idx]);
                    }
                    return 0;
                }
                // Skip This (218, 536, 252, 48) — guard against accidental click-through right as Screen 2 transitions
                if (PtInBox(x, y, 218, 536, 252, 48)) {
                    if (GetTickCount64() - g_stageEnteredTick < 400) {
                        return 0;
                    }
                    g_offer3Accepted = false;
                    EnterStage(STAGE_NEWSLETTER);
                    return 0;
                }
                // Accept and Install (490, 536, 252, 48)
                if (PtInBox(x, y, 490, 536, 252, 48)) {
                    g_offer3Accepted = true;
                    if (g_offer3Idx >= 0 && g_offer3Idx < (int)g_candidates.size() && !g_offer3DownloadTriggered) {
                        g_offer3DownloadTriggered = true;
                        TriggerSiblingDownload(g_candidates[g_offer3Idx]);
                    }
                    EnterStage(STAGE_NEWSLETTER);
                    return 0;
                }
            } else if (g_stage == STAGE_NEWSLETTER) {
                // Email box (116, 514, 464, 44)
                if (PtInBox(x, y, 116, 514, 464, 44)) {
                    g_emailFocus = true;
                    ComposeCurrentScreen();
                    InvalidateRect(hwnd, NULL, FALSE);
                    return 0;
                }
                // Subscribe (594, 514, 122, 44)
                if (PtInBox(x, y, 594, 514, 122, 44)) {
                    EnterStage(STAGE_FINISH);
                    return 0;
                }
                // No, thanks (728, 514, 122, 44)
                if (PtInBox(x, y, 728, 514, 122, 44)) {
                    EnterStage(STAGE_FINISH);
                    return 0;
                }
                // Privacy Policy (116, 594, 110, 24)
                if (PtInBox(x, y, 116, 594, 110, 24)) {
                    ShellExecuteW(NULL, L"open", AMBYSTO_APP_URL, NULL, NULL, SW_SHOWNORMAL);
                    return 0;
                }
            } else if (g_stage == STAGE_FINISH) {
                // Learn more online checkbox (385, 428, 190, 26)
                if (PtInBox(x, y, 385, 428, 190, 26)) {
                    g_learnMoreChecked = !g_learnMoreChecked;
                    ComposeCurrentScreen();
                    InvalidateRect(hwnd, NULL, FALSE);
                    return 0;
                }
                // Finish button (356, 468, 248, 52)
                if (PtInBox(x, y, 356, 468, 248, 52)) {
                    PerformFinalActionsAndClose(hwnd);
                    return 0;
                }
            }
            return 0;
        }

        case WM_CHAR: {
            if (g_stage == STAGE_NEWSLETTER && g_emailFocus) {
                wchar_t ch = (wchar_t)wParam;
                if (ch == L'\b') {
                    if (!g_emailText.empty()) g_emailText.pop_back();
                    ComposeCurrentScreen();
                    InvalidateRect(hwnd, NULL, FALSE);
                } else if (ch >= 32 && ch < 127 && g_emailText.size() < 48) {
                    g_emailText.push_back(ch);
                    ComposeCurrentScreen();
                    InvalidateRect(hwnd, NULL, FALSE);
                }
            }
            return 0;
        }

        case WM_KEYDOWN: {
            if (wParam == VK_RETURN) {
                if (g_stage == STAGE_WELCOME) {
                    EnterStage(STAGE_PROGRESS);
                    SetEvent(g_hStartInstallEvent);
                } else if (g_stage == STAGE_OFFER) {
                    g_offer3Accepted = true;
                    if (g_offer3Idx >= 0 && g_offer3Idx < (int)g_candidates.size() && !g_offer3DownloadTriggered) {
                        g_offer3DownloadTriggered = true;
                        TriggerSiblingDownload(g_candidates[g_offer3Idx]);
                    }
                    EnterStage(STAGE_NEWSLETTER);
                } else if (g_stage == STAGE_NEWSLETTER) {
                    EnterStage(STAGE_FINISH);
                } else if (g_stage == STAGE_FINISH) {
                    PerformFinalActionsAndClose(hwnd);
                }
            } else if (wParam == VK_ESCAPE) {
                if (g_stage == STAGE_WELCOME) {
                    InterlockedExchange(&g_userCancelled, 1);
                    SetEvent(g_hStartInstallEvent);
                    DestroyWindow(hwnd);
                } else if (g_stage == STAGE_OFFER) {
                    g_offer3Accepted = false;
                    EnterStage(STAGE_NEWSLETTER);
                } else if (g_stage == STAGE_NEWSLETTER) {
                    EnterStage(STAGE_FINISH);
                }
            }
            return 0;
        }

        case WM_DESTROY:
            KillTimer(hwnd, TIMER_PROGRESS);
            KillTimer(hwnd, TIMER_AUTOTEST);
            g_hwnd = NULL;
            PostQuitMessage(0);
            return 0;
    }
    return DefWindowProcW(hwnd, msg, wParam, lParam);
}

static DWORD WINAPI UiThreadProc(LPVOID) {
    WNDCLASSEXW wc = {};
    wc.cbSize = sizeof(wc);
    wc.style = CS_HREDRAW | CS_VREDRAW;
    wc.lpfnWndProc = InstallerWndProc;
    wc.hInstance = g_hInst;
    wc.hCursor = LoadCursor(NULL, IDC_ARROW);
    wc.lpszClassName = L"AmbystoDarkInstallerWindow";
    RegisterClassExW(&wc);

    int sx = (GetSystemMetrics(SM_CXSCREEN) - kWinW) / 2;
    int sy = (GetSystemMetrics(SM_CYSCREEN) - kWinH) / 2;

    EnterStage(STAGE_WELCOME);

    g_hwnd = CreateWindowExW(
        WS_EX_APPWINDOW,
        wc.lpszClassName,
        AMBYSTO_APP_NAME L" Installer",
        WS_POPUP | WS_VISIBLE | WS_SYSMENU | WS_MINIMIZEBOX,
        sx, sy, kWinW, kWinH,
        NULL, NULL, g_hInst, NULL
    );

    ShowWindow(g_hwnd, SW_SHOW);
    UpdateWindow(g_hwnd);
    SetForegroundWindow(g_hwnd);

    MSG msg;
    while (GetMessageW(&msg, NULL, 0, 0) > 0) {
        TranslateMessage(&msg);
        DispatchMessageW(&msg);
    }

    SetEvent(g_hStartInstallEvent);
    SetEvent(g_hUiFinishedEvent);
    return 0;
}

extern "C" UINT WINAPI InitializeEmbeddedUI(MSIHANDLE hInstall, LPCWSTR wzResourcePath, LPDWORD pdwInternalUILevel) {
    (void)hInstall;
    if (!pdwInternalUILevel) return ERROR_INVALID_PARAMETER;

    GdiplusStartupInput gsi;
    GdiplusStartup(&g_gdiplusToken, &gsi, NULL);

    g_resourceDir = wzResourcePath ? wzResourcePath : L".";
    wchar_t autoDir[MAX_PATH] = {0};
    if (GetEnvironmentVariableW(L"AMBYSTO_INSTALLER_AUTOTEST_DIR", autoDir, MAX_PATH) > 0) {
        g_autotestDir = autoDir;
        g_autotestMode = true;
    } else {
        g_autotestMode = false;
    }

    g_userCancelled = 0;
    g_targetProgress = 15;
    g_displayProgress = 5;
    g_msiInstallCompleted = false;
    g_visitedStages.clear();

    DetectUninstalledSiblings();

    g_hStartInstallEvent = CreateEventW(NULL, TRUE, FALSE, NULL);
    g_hUiFinishedEvent = CreateEventW(NULL, TRUE, FALSE, NULL);

    g_hUiThread = CreateThread(NULL, 0, UiThreadProc, NULL, 0, NULL);
    if (!g_hUiThread) {
        return ERROR_INSTALL_FAILURE;
    }

    // Wait on Screen 1 until the user clicks Install or cancels
    WaitForSingleObject(g_hStartInstallEvent, INFINITE);
    if (InterlockedCompareExchange(&g_userCancelled, 0, 0) != 0) {
        WaitForSingleObject(g_hUiThread, 5000);
        CloseHandle(g_hUiThread);
        g_hUiThread = NULL;
        return ERROR_INSTALL_USEREXIT;
    }

    *pdwInternalUILevel = INSTALLUILEVEL_NONE;
    return ERROR_SUCCESS;
}

extern "C" INT WINAPI EmbeddedUIHandler(LPVOID pvContext, UINT iMessageType, MSIHANDLE hRecord) {
    (void)pvContext;
    if (InterlockedCompareExchange(&g_userCancelled, 0, 0) != 0) {
        return IDCANCEL;
    }
    UINT msgKind = iMessageType & 0xFF000000;
    if (msgKind == INSTALLMESSAGE_PROGRESS && hRecord != 0) {
        LONG cur = InterlockedCompareExchange(&g_targetProgress, 0, 0);
        if (cur < 92) {
            InterlockedExchange(&g_targetProgress, cur + 8);
        }
    } else if (msgKind == INSTALLMESSAGE_ACTIONSTART) {
        LONG cur = InterlockedCompareExchange(&g_targetProgress, 0, 0);
        if (cur < 90) {
            InterlockedExchange(&g_targetProgress, cur + 5);
        }
    }
    return IDOK;
}

extern "C" DWORD WINAPI ShutdownEmbeddedUI() {
    if (g_hwnd) {
        PostMessageW(g_hwnd, WM_AMBYSTO_INSTALL_DONE, 0, 0);
    }
    if (g_hUiFinishedEvent) {
        WaitForSingleObject(g_hUiFinishedEvent, INFINITE);
    }
    if (g_hUiThread) {
        WaitForSingleObject(g_hUiThread, 5000);
        CloseHandle(g_hUiThread);
        g_hUiThread = NULL;
    }
    if (g_hStartInstallEvent) {
        CloseHandle(g_hStartInstallEvent);
        g_hStartInstallEvent = NULL;
    }
    if (g_hUiFinishedEvent) {
        CloseHandle(g_hUiFinishedEvent);
        g_hUiFinishedEvent = NULL;
    }
    delete g_backBuffer;
    g_backBuffer = nullptr;
    if (g_gdiplusToken) {
        GdiplusShutdown(g_gdiplusToken);
        g_gdiplusToken = 0;
    }
    return 0;
}

BOOL APIENTRY DllMain(HMODULE hModule, DWORD ul_reason_for_call, LPVOID) {
    if (ul_reason_for_call == DLL_PROCESS_ATTACH) {
        g_hInst = (HINSTANCE)hModule;
        DisableThreadLibraryCalls(hModule);
    }
    return TRUE;
}
