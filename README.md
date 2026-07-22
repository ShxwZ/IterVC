# IterVC

<p align="center">
  <img src="./IterVC.Desktop/Assets/logo_ivc3.svg" alt="IterVC Logo" width="180" />
</p>

<p align="center">
  <a href="https://github.com/ShxwZ/IterVC/releases/latest">
    <img src="https://img.shields.io/github/v/release/ShxwZ/IterVC?style=for-the-badge&label=Download%20Latest%20Release" alt="Latest Release">
  </a>
    <a href="https://github.com/ShxwZ/IterVC/releases">
    <img src="https://img.shields.io/github/downloads/ShxwZ/IterVC/total?style=for-the-badge&label=Total%20Downloads&logo=github&color=blue"
         alt="Total Downloads">
  </a>
</p>

<p align="center">
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
    <img src="https://img.shields.io/badge/License-IterVC%20Custom-orange?style=for-the-badge"
         alt="IterVC Custom License">
  </a>

  <a href="https://ko-fi.com/shawz">
    <img src="https://img.shields.io/badge/Ko--fi-Support-ff5f5f?style=for-the-badge&logo=ko-fi&logoColor=white" alt="Ko-fi">
  </a>
</p>
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
dotnet run --project IterVC.Desktop
```

---

## 🛠️ Project Architecture

### IterVC.Core

Core models, settings, and interfaces.

Examples:

- `AudioAppInfo`
- `AudioDeviceInfo`
- Service contracts and shared abstractions

This project contains no UI or platform-specific logic.

### IterVC.Audio

The audio engine responsible for:

- WASAPI session enumeration
- Process-specific loopback capture
- Native Windows audio interop
- Microphone capture
- Audio resampling and normalization
- Real-time mixing using NAudio

### IterVC.Desktop

The Avalonia UI frontend containing:

- Views and ViewModels
- Dependency Injection configuration
- Application state management
- User interaction logic

---

## 📸 Screenshots

<table>
  <tr>
    <td width="50%" align="center">
      <a href="./Screenshots/screenshot-001.png">
        <img src="./Screenshots/screenshot-001.png" alt="Audio device configuration" width="100%" />
      </a>
      <br />
      <sub><strong>Audio devices</strong></sub>
    </td>
    <td width="50%" align="center">
      <a href="./Screenshots/screenshot-002.png">
        <img src="./Screenshots/screenshot-002.png" alt="Application and microphone mix controls" width="100%" />
      </a>
      <br />
      <sub><strong>Mix controls</strong></sub>
    </td>
  </tr>
  <tr>
    <td width="50%" align="center">
      <a href="./Screenshots/screenshot-003.png">
        <img src="./Screenshots/screenshot-003.png" alt="Microphone noise gate configuration" width="100%" />
      </a>
      <br />
      <sub><strong>Microphone noise gate</strong></sub>
    </td>
    <td width="50%" align="center">
      <a href="./Screenshots/screenshot-004.png">
        <img src="./Screenshots/screenshot-004.png" alt="OSC Chatbox integration settings" width="100%" />
      </a>
      <br />
      <sub><strong>OSC integration</strong></sub>
    </td>
  </tr>
</table>

---

## ⚙️ How It Works

When an application is selected in the UI:

1. IterVC creates a dedicated process loopback capture session for the target process.
2. Audio is captured non-destructively from the application's output stream.
3. Captured audio is converted to a common format 
4. The audio is mixed with the selected microphone input.
5. The resulting stream is sent to the configured virtual audio device.

Because the capture uses Windows Process Loopback APIs, the original application continues to play audio normally through the user's speakers or headphones.

One can also display a chatbox on VRChat by using the OSC tab on the app. There are three tokens one can use:

1. {title} displays the song title and the artist.
2. {status} displays wether you are playing the song or not.
3. {time} displays your current local time and it also displays the song's timestamp. 

Support for having the chatbox opened without playing music is planned for a future release. As of now one must play music to make the chatbox appear.

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
