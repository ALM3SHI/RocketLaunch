# RocketLaunch

لانشر مخصص للعب Rocket League Workshop maps مع أصدقائك عبر الإنترنت بدون Hamachi أو Radmin VPN.

---

## كيف يشتغل

```
أنت                           صاحبك
 │                               │
 │  يفتح LauncherApp             │  يفتح LauncherApp
 │  يضغط "Host Game"             │
 │  ← ينشئ شبكة ZeroTier         │
 │  ← يرسل دعوة عبر              │  ← يستقبل الدعوة
 │    PresenceServer             │  ← ينضم للشبكة تلقائيًا
 │                               │
 │  كلاكما على نفس               │
 │  LAN افتراضي (10.x.x.x)      │
 │                               │
 │  Launch Rocket League         │  Launch Rocket League
 │  → LAN Match                  │  → يشوف المباراة في القائمة
```

---

## المتطلبات

### على **كلا** الجهازين:

1. **[ZeroTier One](https://www.zerotier.com/download/)** — ثبّته وابدأ الخدمة
   ```
   # تحقق إنه شغال (في PowerShell كـ Admin):
   zerotier-cli info
   # يفترض يطلع: 200 info <nodeId> <version> ONLINE
   ```

2. **حساب ZeroTier Central** (مجاني) على [my.zerotier.com](https://my.zerotier.com)
   - اذهب إلى **Account → API Access Tokens → New Token**
   - احفظ الـ token (ما تقدر ترجع تشوفه مرة ثانية)

3. **.NET 8 Runtime** (أو أحدث) — [تنزيل](https://dotnet.microsoft.com/download/dotnet/8.0)

4. **Rocket League** — Steam أو Epic

---

## تشغيل المشروع (Development)

```powershell
# 1. شغّل السيرفر (نافذة PowerShell منفصلة)
cd src\PresenceServer
dotnet run

# 2. شغّل التطبيق (نافذة PowerShell أخرى)
cd src\LauncherApp
dotnet run
```

> للعب مع صاحبك: وجّه PresenceServer لسيرفر عام (Render، Fly.io، إلخ)
> ثم غيّر `ServerUrl` في `PresenceClient.cs` ليشير للسيرفر المنشور.

---

## خطوات الاستخدام

### أول مرة فقط:
1. افتح التطبيق → سيظهر مربع "ZeroTier API Token"
2. الصق الـ token من my.zerotier.com → اضغط **Save**
3. تبادل "Friend Code" مع صاحبك (يظهر في الزاوية العلوية اليمنى)
4. كلاكما يضيف كود الثاني في حقل "Add Friend"

### كل جلسة:
| الخطوة | أنت (Host) | صاحبك (Guest) |
|--------|-----------|--------------|
| 1 | اضغط **🔴 Host Game** | ينتظر الدعوة |
| 2 | اضغط **Invite** بجانب اسم صاحبك | — |
| 3 | — | يضغط **Yes** على نافذة الدعوة |
| 4 | اضغط **🎮 Launch Rocket League** | اضغط **🎮 Launch Rocket League** |
| 5 | داخل اللعبة: Extras → Custom Training → Workshop, أو Multiplayer → LAN | داخل اللعبة: Multiplayer → LAN → يشوف مبارياتك |

---

## هيكل المشروع

```
RocketLaunch/
└── src/
    ├── PresenceServer/          # ASP.NET Core + SignalR
    │   ├── Hubs/
    │   │   └── PresenceHub.cs   # من أونلاين، تمرير الدعوات
    │   └── Program.cs
    │
    └── LauncherApp/             # WPF (.NET 8)
        ├── Services/
        │   ├── ZeroTierService.cs     # إنشاء/انضمام شبكات ZeroTier
        │   ├── GameLauncherService.cs # اكتشاف وتشغيل Rocket League
        │   ├── PresenceClient.cs      # SignalR client
        │   └── LocalProfile.cs        # حفظ الإعدادات محليًا
        ├── Converters/
        │   ├── OnlineToColorConverter.cs
        │   └── BoolToVisibilityConverter.cs
        ├── InputDialog.cs       # WPF input dialog مخصص
        ├── MainWindow.xaml      # الواجهة
        └── MainWindow.xaml.cs   # المنطق
```

---

## المراحل

| المرحلة | الحالة | الوصف |
|---------|--------|-------|
| 1 | ✅ مكتملة | Presence server + friend list + invites |
| 2 | ✅ مكتملة | ZeroTier virtual LAN (auto create/join) |
| 3 | ✅ مكتملة | Rocket League auto-detection & launch |
| 4 | 🔜 | Hosted PresenceServer + installer (MSIX) |

---

## ملاحظات تقنية

- **ZeroTier**: الشبكة الافتراضية تعمل على مستوى Layer 2 (Ethernet)، مما يجعل الجهازين يظهران كأنهما على نفس الشبكة المحلية حرفيًا — هذا ما تحتاجه Rocket League لـ LAN discovery.
- **Auto-authorization**: عند الانضمام، السيرفر (Host) يستطلع Central API كل 5 ثواني ويوافق على الأجهزة الجديدة تلقائيًا — لا حاجة لتسجيل دخول يدوي في my.zerotier.com.
- **EAC**: التطبيق لا يحقن نفسه في العملية، فقط يشغل اللعبة عبر Steam/Epic URI — آمن من Easy Anti-Cheat.
