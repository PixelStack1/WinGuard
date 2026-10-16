# WinGuard

**Reprends le contrôle de ta vie privée sur Windows 11.** / **Take back control of your privacy on Windows 11.**

![WinGuard](docs/screenshot.png)

WinGuard est une application gratuite et open source qui désactive la télémétrie, la pub, le suivi et les petits trucs intrusifs de Windows 11 — simplement, en quelques clics. Pensée pour tout le monde : tu coches ce que tu veux, tu appliques, c'est réglé. Et si un réglage ne te plaît pas, tu reviens en arrière en un clic.

*WinGuard is a free, open-source app that disables Windows 11 telemetry, ads, tracking and other intrusive bits — simply, in a few clicks. Tick what you want, apply, done. Don't like a tweak? Roll it back in one click.*

---

## 🤔 À quoi sert WinGuard ? / What is WinGuard for?

Par défaut, Windows 11 envoie en permanence des informations à Microsoft : ce que tu tapes, les applis que tu ouvres, ta localisation, des identifiants publicitaires, des rapports d'utilisation... Beaucoup de ces réglages sont activés d'origine et bien cachés dans les menus.

**WinGuard rassemble tous ces réglages au même endroit** et te laisse les désactiver d'un simple clic, sans avoir à fouiller dans le registre ou les stratégies de groupe. Concrètement, il te permet de :

- **couper la télémétrie et le suivi** (diagnostics, frappe au clavier, historique d'activité) ;
- **supprimer la publicité** dans le menu Démarrer, l'explorateur et l'écran de verrouillage ;
- **désactiver Cortana, le suivi de localisation** et les applis en arrière-plan inutiles ;
- **renforcer ta sécurité réseau** (DNS chiffré, blocage par fichier hosts, pare-feu) ;
- **nettoyer (debloat)** les applications préinstallées dont tu ne te sers pas.

En clair : **un Windows plus privé, plus léger et plus rapide**, sans y connaître quoi que ce soit en technique. Et tout est réversible à tout moment.

*By default, Windows 11 constantly sends data to Microsoft — what you type, which apps you open, your location, advertising IDs, usage reports... WinGuard gathers all those settings in one place and lets you turn them off in a single click: telemetry, ads, Cortana, location tracking, background apps, and more. The result: a more private, lighter and faster Windows — no technical knowledge needed, and everything is reversible.*

## ✨ Ce qu'il fait / What it does

- **152 protections** couvrant la télémétrie, la publicité, Cortana, le suivi de localisation, les services en arrière-plan, et bien plus.
- **Profils** prêts à l'emploi (recommandé, strict, etc.).
- **Point de restauration** automatique avant application : zéro risque.
- **DNS-over-HTTPS**, blocage par fichier hosts, règles de pare-feu sortantes.
- **Interface FR / EN**, thème sombre moderne.
- **100 % hors ligne, 100 % respectueux de ta vie privée** — WinGuard ne collecte rien.

## 🚀 Installation

👉 Télécharge **`WinGuardSetup.exe`** depuis la section [Releases](../../releases), lance-le, et laisse-toi guider.

Guide détaillé : [`INSTALL/INSTALLATION-FR.md`](INSTALL/INSTALLATION-FR.md) · [English guide](INSTALL/INSTALLATION-EN.md)

> ⚠️ Windows peut afficher un avertissement SmartScreen (l'appli n'est pas encore signée avec un certificat payant). Clique sur **« Informations complémentaires » → « Exécuter quand même »**.

## 🛠️ Pour les développeurs / For developers

Projet **C# / WPF / .NET 8**, architecture MVVM. Pour compiler :

```bash
dotnet build WinGuard.sln -c Release
```

L'installeur est généré avec **Inno Setup** (`WinGuard.iss`).

## 📄 Licence

À définir. / To be defined.

---

Fait avec soin — **by Corpse** ❤️
