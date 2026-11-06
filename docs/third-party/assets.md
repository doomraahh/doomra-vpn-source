# Встроенные ресурсы

JetBrains Mono Regular/Bold: https://github.com/JetBrains/JetBrainsMono/tree/02bb50b082dad9ef8a0f33ac393839202b760223 — SIL OFL 1.1, полный текст JetBrainsMono-OFL.txt. Файлы шрифта не изменены.

Флаги: https://github.com/hampusborgos/country-flags/tree/ba2cf4101bf029d2ada26da2f95121de74581a4d — PNG из png250px и png1000px и countries.json. Публичное достояние согласно README проекта, копия CountryFlags-README.md.

Логотип doomra: предоставлен пользователем, assets/doomra-logo.png. Используется исходное изображение с прозрачностью. Значок создаётся tools/CreateIcon.cs из этого же изображения.

Вертикальные композиции США, Нидерландов, Германии, Швеции и Финляндии: SVG предоставлены пользователем, assets/custom-flags/{us,nl,de,se,fi}.svg. В PNG для встроенного ресурса переведены без изменения композиции; средства сборки tools/prepare-custom-flags.ps1 и RenderFlagAsset.cs поддерживают только применённые простые векторные элементы. Эти изображения используются для фона карточки, обычные пропорциональные значки сохранены.
