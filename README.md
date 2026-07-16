# IterVC

<p align="center">
  <img src="./RadioOSC.Desktop/Assets/logo_ivc3.svg" alt="IterVC Logo" width="180" />
</p>

<p align="center">
  <a href="https://github.com/ShxwZ/IterVC/releases/latest">
    <img src="https://img.shields.io/github/v/release/ShxwZ/IterVC?style=for-the-badge&label=Download%20Latest%20Release" alt="Latest Release">
  </a>
</p>

<p align="center">
  <a href="https://github.com/ShxwZ/IterVC/stargazers"> 
    <img src="https://img.shields.io/github/stars/ShxwZ/IterVC?style=for-the-badge" alt="Stars">
  </a>
  <a href="https://github.com/ShxwZ/IterVC/network/members">
    <img src="https://img.shields.io/github/forks/ShxwZ/IterVC?style=for-the-badge" alt="Forks">
  </a>
  <a href="https://github.com/ShxwZ/IterVC/issues">
    <img src="https://img.shields.io/github/issues/ShxwZ/IterVC?style=for-the-badge" alt="Issues">
  </a>
  <a href="LICENSE">
    <img src="https://img.shields.io/github/license/ShxwZ/IterVC?style=for-the-badge" alt="License">
  </a>
  <a href="https://ko-fi.com/shawz">
    <img src="https://img.shields.io/badge/Ko--fi-Support-ff5f5f?style=for-the-badge&logo=ko-fi&logoColor=white" alt="Ko-fi">
  </a>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/.NET-8.0-512BD4?style=for-the-badge&logo=dotnet" alt=".NET 8">
  <img src="https://img.shields.io/badge/Avalonia-UI-6C2DC7?style=for-the-badge" alt="Avalonia">
  <img src="https://img.shields.io/badge/Windows-10%20%2F%2011-0078D6?style=for-the-badge&logo=windows" alt="Windows">
  <img src="https://img.shields.io/badge/x64-Only-blue?style=for-the-badge" alt="x64">




> *"Iter" (Latin for path/journey) + "VC" (Virtual Cable)*

**IterVC** is a lightweight desktop application built with **.NET 8** and **Avalonia UI** that allows you to select specific running applications, capture their audio, mix it with your physical microphone, and route the combined stream to a virtual audio device so applications such as Discord, OBS Studio, TeamSpeak, or VRChat can detect it as a regular microphone.

The application also includes optional OSC (Open Sound Control) integration for VRChat. Through the VRChat Chatbox, IterVC can automatically display configurable information such as the currently playing track, playback time, duration, or custom messages, allowing users to share media information directly in-game.

Whether you're hanging out with friends, hosting events, roleplaying, or simply looking for a flexible virtual audio routing solution, IterVC aims to provide a simple and reliable experience.

**Note:** Selected applications continue to play audio normally through your speakers or default output device. IterVC never mutes, redirects, or hijacks audio playback—it simply captures a non-destructive copy of the audio stream.

---

## 🔌 Prerequisites

- **Windows 10 (Build 19041 / Version 2004) or later**, or **Windows 11**
  - Required for the native WASAPI Process Loopback API.
- **64-bit (x64) Windows**
  - The native interop layer requires a 64-bit environment.
- **.NET 8 SDK**
- **Virtual Audio Cable (Recommended)**
  - IterVC was developed and tested primarily with VB-Audio Virtual Cable (CABLE Input / CABLE Output). While other virtual audio devices may work, VB-Cable is the recommended configuration.

---

## 🚀 Build & Run

From the repository root:

```bash
dotnet restore
dotnet build
dotnet run --project RadioOSC.Desktop
```

---

## 🛠️ Project Architecture

### RadioOSC.Core

Core models, settings, and interfaces.

Examples:

- `AudioAppInfo`
- `AudioDeviceInfo`
- Service contracts and shared abstractions

This project contains no UI or platform-specific logic.

### RadioOSC.Audio

The audio engine responsible for:

- WASAPI session enumeration
- Process-specific loopback capture
- Native Windows audio interop
- Microphone capture
- Audio resampling and normalization
- Real-time mixing using NAudio

### RadioOSC.Desktop

The Avalonia UI frontend containing:

- Views and ViewModels
- Dependency Injection configuration
- Application state management
- User interaction logic

---

## ⚙️ How It Works

When an application is selected in the UI:

1. IterVC creates a dedicated process loopback capture session for the target process.
2. Audio is captured non-destructively from the application's output stream.
3. Captured audio is converted to a common format 
4. The audio is mixed with the selected microphone input.
5. The resulting stream is sent to the configured virtual audio device.

Because the capture uses Windows Process Loopback APIs, the original application continues to play audio normally through the user's speakers or headphones.

---

## 🤝 Contributing

Feedback, bug reports, feature requests, and pull requests are always welcome.

If you encounter an issue, have an idea for an improvement, or want to contribute to the project, feel free to:

- Open an Issue
- Submit a Pull Request
- Share suggestions and feedback

Every contribution helps improve IterVC for everyone.

---

## 📄 License

This project is licensed under the terms specified in the repository's license file.
