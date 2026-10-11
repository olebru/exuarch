const exuarchScreens = new WeakMap();
const exuarchTurns = new MessageChannel();
const exuarchWaiting = [];
exuarchTurns.port1.onmessage = () => exuarchWaiting.shift()?.();

window.exuarchScreen = {
    draw: function (canvasId, x, y, width, height, rows, depth) {
        const canvas = document.getElementById(canvasId);
        if (!canvas) return;
        let screen = exuarchScreens.get(canvas);
        if (!screen) {
            screen = {
                context: canvas.getContext('2d'),
                words: new Uint8Array(canvas.width * canvas.height * 2),
                image: new ImageData(canvas.width, canvas.height),
            };
            exuarchScreens.set(canvas, screen);
        }
        const stride = canvas.width;
        rows.copyTo(screen.words.subarray(y * stride * 2, (y + height) * stride * 2));
        const words = screen.words, rgba = screen.image.data;
        for (let row = y; row < y + height; row++) {
            for (let i = row * stride + x, j = i * 4, end = i + width; i < end; i++, j += 4) {
                if (depth) {
                    rgba[j] = rgba[j + 1] = rgba[j + 2] = 255 - words[2 * i + 1];
                } else {
                    const p = words[2 * i] | (words[2 * i + 1] << 8);
                    const r = (p >> 11) & 0x1F, g = (p >> 5) & 0x3F, b = p & 0x1F;
                    rgba[j] = (r << 3) | (r >> 2);
                    rgba[j + 1] = (g << 2) | (g >> 4);
                    rgba[j + 2] = (b << 3) | (b >> 2);
                }
                rgba[j + 3] = 255;
            }
        }
        screen.context.putImageData(screen.image, 0, 0, x, y, width, height);
    },
    nextTurn: function () {
        return new Promise(resolve => { exuarchWaiting.push(resolve); exuarchTurns.port2.postMessage(0); });
    },
};
