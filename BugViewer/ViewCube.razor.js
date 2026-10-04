class ViewCubeController {
    #overlayElement;
    #rotorElement;
    #canvasElement;
    #faces = [];
    #sizeRatio = 0.125;

    initialize(overlayElement, rotorElement, canvasElement, viewMatrix) {
        this.#overlayElement = overlayElement;
        this.#rotorElement = rotorElement;
        this.#canvasElement = canvasElement;
        this.#faces = overlayElement
            ? Array.from(overlayElement.querySelectorAll('[data-view-cube-normal]'))
            : [];
        this.updateSize();
        this.updateOrientation(viewMatrix);
    }

    updateSize() {
        if (!this.#overlayElement || !this.#canvasElement) return;

        const rect = this.#canvasElement.getBoundingClientRect();
        const size = Math.max(rect.width, rect.height) * this.#sizeRatio;
        if (!Number.isFinite(size) || size <= 0) return;

        this.#overlayElement.style.setProperty('--view-cube-size', `${size}px`);
    }

    updateOptions(sizeRatio, opacity) {
        if (Number.isFinite(sizeRatio) && sizeRatio > 0) {
            this.#sizeRatio = sizeRatio;
        }
        if (this.#overlayElement && Number.isFinite(opacity)) {
            this.#overlayElement.style.opacity = Math.max(0, Math.min(1, opacity)).toString();
        }
        this.updateSize();
    }

    updateOrientation(viewMatrix) {
        if (!this.#rotorElement || !viewMatrix || viewMatrix.length < 16) return;

        const xAxis = normalizeAxis(viewMatrix[0], viewMatrix[1], viewMatrix[2]);
        const yAxis = normalizeAxis(viewMatrix[4], viewMatrix[5], viewMatrix[6]);
        const zAxis = normalizeAxis(viewMatrix[8], viewMatrix[9], viewMatrix[10]);
        const cssMatrix = [
            xAxis[0], -xAxis[1], xAxis[2], 0,
            yAxis[0], -yAxis[1], yAxis[2], 0,
            zAxis[0], -zAxis[1], zAxis[2], 0,
            0, 0, 0, 1
        ];

        this.#rotorElement.style.transform = `matrix3d(${cssMatrix.join(',')})`;
        this.#updateFaceVisibility(xAxis, yAxis, zAxis);
    }

    dispose() {
        this.#faces = [];
        this.#overlayElement = null;
        this.#rotorElement = null;
        this.#canvasElement = null;
    }

    #updateFaceVisibility(xAxis, yAxis, zAxis) {
        for (const face of this.#faces) {
            const normal = face.dataset.viewCubeNormal.split(',').map(Number);
            const cameraZ = normal[0] * xAxis[2] + normal[1] * yAxis[2] + normal[2] * zAxis[2];
            const isVisible = cameraZ > 0.0001;
            face.style.visibility = isVisible ? 'visible' : 'hidden';
            face.style.pointerEvents = isVisible ? 'auto' : 'none';
            face.tabIndex = isVisible ? 0 : -1;
            face.setAttribute('aria-hidden', isVisible ? 'false' : 'true');
        }
    }
}

function normalizeAxis(x, y, z) {
    const length = Math.hypot(x, y, z);
    if (!Number.isFinite(length) || length < 1e-8) return [0, 0, 0];
    return [x / length, y / length, z / length];
}

export function createViewCubeController() {
    return new ViewCubeController();
}
