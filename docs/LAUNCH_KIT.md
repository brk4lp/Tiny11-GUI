# Launch kit

Use these drafts after a tested GitHub release exists. Replace bracketed fields with measured facts; do not advertise an ISO-size reduction that was not reproduced on the named source image.

## GitHub release summary

**Tiny11 GUI [version] — safer builds, multilingual UI, and portable releases**

Tiny11 GUI is a Windows desktop interface for creating a configurable, lighter Windows 11 installation image from media you already own. This release adds fail-fast DISM handling, ownership-scoped recovery, atomic ISO output, eight UI languages, automated tests, and a self-contained portable Windows package with a SHA-256 checksum.

Please test generated media in a virtual machine first. Compatibility reports that include the host build, source image build/language/edition, WIM or ESD format, and selected options are welcome.

## Short English announcement

I built Tiny11 GUI, an open-source WPF app that guides you through creating a configurable, lighter Windows 11 ISO from your own installation media. The current release focuses on safety: checked DISM exit codes, build-owned cleanup, atomic output, automated tests, and eight UI languages. Feedback from different Windows builds and ISO languages would be genuinely useful: [release link]

## Kısa Türkçe duyuru

Kendi Windows 11 medyanızdan yapılandırılabilir ve daha hafif bir kurulum ISO'su hazırlamayı kolaylaştıran açık kaynak WPF uygulaması Tiny11 GUI'yi geliştirdim. Bu sürüm özellikle güvenli build akışına odaklanıyor: DISM hata kodu kontrolleri, yalnızca uygulamanın sahip olduğu kaynakları temizleme, atomik ISO çıktısı, otomatik testler ve sekiz arayüz dili. Farklı Windows sürümleri ve ISO dilleriyle geri bildirim çok değerli: [release bağlantısı]

## Suggested demo sequence

Keep a screen recording under 45 seconds:

1. Open the app and switch the UI language.
2. Select a redacted sample ISO path and show edition discovery.
3. Select the Balanced preset, then briefly compare two advanced options.
4. Start a build and show the structured live log.
5. End on a verified output file and the GitHub Releases page.

Never record a real product key, personal path, unattended credential, or proprietary Windows image content.

## Feedback call

Ask for one concrete action per post: star the project, test a specific release, or submit a structured compatibility report. Compatibility reports are most useful when they include:

- host Windows version and whether it is stock or trimmed;
- source ISO build, language, edition, and WIM/ESD format;
- preset and changed options;
- success/failure stage and sanitized log excerpt;
- resulting ISO size only as supporting context, not proof of correctness.
