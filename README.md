# RocketLaunch

**لانشر مخصص للعب Rocket League Workshop maps مع أصدقائك عبر الإنترنت — بدون Hamachi أو Radmin VPN.**

---

## كيف يشتغل

```
أنت (Host)                       صاحبك (Guest)
───────────                       ──────────────
يفتح RocketLaunch                 يفتح RocketLaunch
يضغط [Host Game]
   └─ ينشئ شبكة ZeroTier
   └─ يرسل دعوة                ──► يستقبل الدعوة ويضغط Yes
                                    └─ ينضم للشبكة تلقائياً

كلاكما على نفس LAN افتراضي (10.x.x.x)

يضغطان [Launch Rocket League]
   └─ اللعبة تفتح               ──► اللعبة تفتح
   └─ يكوّن LAN Match               └─ يشوف المباراة في القائمة
```

---

## المتطلبات (على **كلا** الجهازين)

| المتطلب | الرابط | ملاحظة |
|---------|--------|--------|
| **ZeroTier One** | [zerotier.com/download](https://www.zerotier.com/download/) | يجب تشغيله كخدمة |
| **ZeroTier Central account** | [my.zerotier.com](https://my.zerotier.com) | مجاني — لإنشاء API token |
| **.NET 8 Runtime** | [dotnet.microsoft.com](https://dotnet.microsoft.com/download/dotnet/8.0) | مطلوب لتشغيل التطبيق |
| **Rocket League** | Steam أو Epic | |

### الحصول على ZeroTier API Token
1. سجّل دخول على [my.zerotier.com](https://my.zerotier.com)
2. **Account** → **API Access Tokens** → **New Token**
3. احفظ الـ token — لن تراه مرة ثانية

---

## التشغيل السريع (Development)

```powershell
# نافذة 1 — السيرفر
cd src\PresenceServer
dotnet run

# نافذة 2 — التطبيق
cd src\LauncherApp
dotnet run
```

---

## النشر على الإنترنت (Render.com — مجاني)

### 1. ارفع المشروع على GitHub
```bash
git remote add origin https://github.com/USERNAME/RocketLaunch.git
git push -u origin master
```

### 2. انشر PresenceServer
1. سجّل دخول على [render.com](https://render.com)
2. **New** → **Web Service** → **Deploy from Git**
3. اختر الـ repo → Render يكتشف `render.yaml` تلقائياً
4. انتظر الـ deploy (2-3 دقائق) → ستحصل على URL مثل:
   ```
   https://rocketlaunch-presence.onrender.com
   ```

### 3. وجّه التطبيق للسيرفر
في جهاز **كل** مستخدم، اضبط متغير البيئة:
```powershell
# PowerShell (دائم)
[System.Environment]::SetEnvironmentVariable("ROCKETLAUNCH_SERVER",
    "https://rocketlaunch-presence.onrender.com",
    "User")
```
> **أو**: الـ installer يسألك عن هذا تلقائياً عند التثبيت.

---

## بناء الـ Installer

```powershell
# publish فقط (ينتج LauncherApp.exe في installer\publish)
.\build-release.ps1

# publish + installer (يحتاج Inno Setup 6 مثبت)
.\build-release.ps1 -BuildInstaller
```

### تثبيت Inno Setup (مرة واحدة)
```powershell
# عبر Chocolatey
choco install innosetup

# أو يدوياً من
# https://jrsoftware.org/isdl.php
```

---

## نشر إصدار جديد تلقائياً (GitHub Actions)

```bash
# أي commit بعدها push لـ tag يبني ويرفع الـ installer تلقائياً
git tag 1.0.1
git push origin 1.0.1
```

سيقوم GitHub Actions بـ:
1. بناء الـ installer
2. إنشاء GitHub Release باسم "RocketLaunch 1.0.1"
3. رفع `RocketLaunch-Setup.exe` على الـ Release

---

## هيكل المشروع

```
RocketLaunch/
├── Dockerfile               # لنشر PresenceServer على Render/Docker
├── render.yaml              # Render Blueprint (deploy بضغطة)
├── build-release.ps1        # سكريبت بناء الـ exe والـ installer
├── .github/workflows/
│   └── release.yml          # CI/CD — ينشر تلقائياً عند push tag
├── installer/
│   └── RocketLaunch.iss     # Inno Setup script
└── src/
    ├── PresenceServer/      # ASP.NET Core + SignalR
    │   ├── Hubs/PresenceHub.cs
    │   └── Program.cs       # يقرأ PORT من env var
    └── LauncherApp/         # WPF (.NET 8)
        ├── Services/
        │   ├── ZeroTierService.cs      # إنشاء/انضمام شبكات ZeroTier
        │   ├── GameLauncherService.cs  # اكتشاف وتشغيل Rocket League
        │   ├── PresenceClient.cs       # SignalR client (يقرأ ROCKETLAUNCH_SERVER)
        │   ├── LocalProfile.cs         # حفظ الإعدادات محلياً
        │   └── UpdateService.cs        # تحديث تلقائي من GitHub Releases
        └── MainWindow.xaml(.cs)        # الواجهة الرئيسية
```

---

## المراحل

| # | الحالة | الوصف |
|---|--------|-------|
| 1 | ✅ | Presence server + friend list + invites |
| 2 | ✅ | ZeroTier virtual LAN (auto create/join/authorize) |
| 3 | ✅ | Rocket League detection & launch (Epic + Steam) |
| 4 | ✅ | Dockerfile + Render deploy + installer + auto-update + CI/CD |

---

## ملاحظات تقنية

- **ZeroTier Layer 2**: الشبكة الافتراضية تعمل على مستوى Ethernet — الجهازان يظهران كأنهما على نفس شبكة محلية حرفياً.
- **Auto-authorize**: الـ Host يستطلع Central API كل 5 ثواني ويوافق على أجهزة الـ Guests تلقائياً.
- **EAC Safe**: التطبيق يشغّل اللعبة عبر Steam/Epic URI فقط — لا حقن في العملية.
- **ROCKETLAUNCH_SERVER**: يُقرأ عند كل تشغيل — غيّر قيمته ويُطبّق فوراً بدون إعادة بناء.
