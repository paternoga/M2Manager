using System.Runtime.CompilerServices;

// Testy sprawdzają też logikę pomocniczą (parser OCR, normalizacja nagłówków, sortowanie listy),
// która nie ma powodu być częścią publicznego API projektu.
[assembly: InternalsVisibleTo("M2Manager.Tests")]
