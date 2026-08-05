// Minimalny JS interop dla edytora rzutu.
// W czystym Blazorze nie da się przeliczyć współrzędnych myszy/palca na układ SVG —
// tylko po to jest ten plik. Cała logika edycji zostaje w C#.

window.m2plan = {
    /**
     * Ile jednostek viewBoksa (centymetrów) przypada na jeden piksel ekranu.
     * SVG ma atrybuty width/height zgodne z proporcją viewBoksa i CSS `height:auto`,
     * więc skala jest jednakowa w obu osiach.
     */
    unitsPerPixel: function (svgEl) {
        if (!svgEl) {
            return 1;
        }

        const rect = svgEl.getBoundingClientRect();
        const box = svgEl.viewBox.baseVal;

        if (!rect.width || !box.width) {
            return 1;
        }

        return box.width / rect.width;
    },

    /** Punkt kliknięcia przeliczony na współrzędne rzutu (w centymetrach). */
    toSvgPoint: function (svgEl, clientX, clientY) {
        if (!svgEl) {
            return { x: 0, y: 0 };
        }

        const rect = svgEl.getBoundingClientRect();
        const box = svgEl.viewBox.baseVal;

        if (!rect.width || !rect.height || !box.width || !box.height) {
            return { x: 0, y: 0 };
        }

        const scaleX = box.width / rect.width;
        const scaleY = box.height / rect.height;

        return {
            x: box.x + (clientX - rect.left) * scaleX,
            y: box.y + (clientY - rect.top) * scaleY
        };
    },

    /** Przechwycenie wskaźnika — dzięki temu przeciąganie nie gubi się poza elementem. */
    capturePointer: function (element, pointerId) {
        if (element && element.setPointerCapture) {
            try {
                element.setPointerCapture(pointerId);
            } catch {
                // Przeglądarka może odmówić (np. gdy wskaźnik już zniknął) — to nie jest błąd krytyczny.
            }
        }
    },

    releasePointer: function (element, pointerId) {
        if (element && element.releasePointerCapture) {
            try {
                element.releasePointerCapture(pointerId);
            } catch {
                // jw.
            }
        }
    }
};
