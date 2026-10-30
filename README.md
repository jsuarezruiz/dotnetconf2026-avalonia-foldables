# Avalonia on foldables

Two apps from our .NET Conf 2026 session, built with Avalonia and [Nova.Avalonia.Foldable](https://github.com/jsuarezruiz/Nova.Avalonia.Foldable). They run on the iPhone Duo, Android foldables and the desktop, where a simulator folds the phone in 3D.

## Bot vs Bugs

Launch the dotnet bot at the bugs before they ship. Drag it back on the sling, or fold the phone to pull it and snap it open to fire. Laid flat, the game spans both screens. In tabletop, the bottom half becomes a control deck.

<img src="images/bot-vs-bugs-open.png" width="49%" alt="Bot vs Bugs across the fold of an open iPhone Duo"> <img src="images/bot-vs-bugs-book.png" width="49%" alt="Bot vs Bugs on a half-folded iPhone Duo, the fold pulling the sling">

## Inkfold

A sketchbook for a phone that folds. The page opens across the fold like a book, and closing the phone presses the wet paint onto the facing page.

<img src="images/inkfold-open.png" width="49%" alt="Inkfold with a pressed, mirrored drawing across the fold"> <img src="images/inkfold-book.png" width="49%" alt="Inkfold on a half-folded iPhone Duo">

## Run them

You need .NET SDK 10.0.402 with the iOS and Android workloads, and Xcode 27.1 for iOS. Clone with the library, which comes in as a submodule until it's on NuGet:

```bash
git clone --recursive https://github.com/jsuarezruiz/dotnetconf2026-avalonia-foldables.git
dotnet run --project src/BotVsBugs/BotVsBugs.Desktop
```

Swap in `Inkfold` for the paint app, or build the `.iOS` and `.Android` heads for a device. [STATUS.md](STATUS.md) lists what's been tested where.

## Credits

Art from [Kenney](https://kenney.nl) (CC0), the [.NET bot](https://github.com/dotnet/brand) (CC0), the [Lilita One](https://fonts.google.com/specimen/Lilita+One) font (OFL), [Lucide](https://lucide.dev) icons (ISC) and [Aether.Physics2D](https://github.com/nkast/Aether.Physics2D) (MS-PL). The art, font and icon licenses are in [licenses](licenses). The code is [MIT](LICENSE).
