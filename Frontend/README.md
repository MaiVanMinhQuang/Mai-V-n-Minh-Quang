# TOEIC Reading learner app

Flutter/Dart mobile app for learners. It lists published TOEIC Reading exams,
supports timed attempts with automatically saved answers, and shows the score and
answer review after submission. Learners do not need to sign in.

## Run on an Android emulator

Start the ASP.NET Core API and SQL Server first, then from this directory run:

```powershell
flutter pub get
flutter run
```

The default API address is `http://10.0.2.2:5055/api`, which reaches the host
computer from an Android emulator. For a physical phone, set the API address to the
host computer's LAN IP:

```powershell
flutter run --dart-define=API_BASE_URL=http://192.168.1.20:5055/api
```

## Run in Chrome

Start the Backend API, then run from this directory:

```powershell
flutter pub get
flutter run -d chrome --web-port 8080 --dart-define=API_BASE_URL=http://localhost:5055/api
```

The Backend's development CORS allowlist includes `http://localhost:8080`.

For deployment, use the server's HTTPS API URL. Do not expose the development HTTP
endpoint to an untrusted network.
