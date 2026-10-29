"""Build the ground detail texture arrays from source photographs-style images.

    python Scripts/build_ground_textures.py <raw folder>

Inputs: grass, dirt, rock, sand, snow, regolith .png (any square size).
Outputs (3x2 flipbooks for Texture2DArray import, slice order left-to-right, top-to-bottom):
  SpaceEngineG/Assets/SpaceEngine/Resources/Textures/GroundDetail.png  linear detail ratio * 0.4
  SpaceEngineG/Assets/SpaceEngine/Resources/Textures/GroundNormal.png  tangent-space normal (RG)

Albedo is stored as a ratio to the layer's mean linear colour, so the shader keeps the
procedural biome colour seen from orbit and only adds micro-structure up close.
"""
import pathlib
import sys

import numpy as np
from PIL import Image

LAYERS = ['grass', 'dirt', 'rock', 'sand', 'snow', 'regolith']
BUMP = {'grass': .9, 'dirt': 1.4, 'rock': 1.7, 'sand': 1.1, 'snow': .32, 'regolith': 1.3}
SIZE = 1024
ROOT = pathlib.Path(__file__).resolve().parent.parent
OUT = ROOT / 'SpaceEngineG/Assets/SpaceEngine/Resources/Textures'


def srgb_to_linear(c):
    return np.where(c <= .04045, c / 12.92, ((c + .055) / 1.055) ** 2.4)


def crossfade_seamless(image, band):
    """Overlap-crossfade the last `band` texels onto the first, per axis, preserving variance."""
    for axis in (1, 0):
        n = image.shape[axis] - band
        head = np.take(image, range(band), axis=axis)
        tail = np.take(image, range(n, n + band), axis=axis)
        t = (np.arange(band) + .5) / band
        t = t.reshape((1, band, 1) if axis == 1 else (band, 1, 1))
        mean = image.mean(axis=(0, 1), keepdims=True)
        mixed = (head * t + tail * (1 - t) - mean) / np.sqrt(t * t + (1 - t) ** 2) + mean
        image = np.take(image, range(n), axis=axis).copy()
        if axis == 1:
            image[:, :band] = mixed
        else:
            image[:band] = mixed
    return image


def blur_wrap(height, sigma):
    """Gaussian blur with wrap-around via FFT, so the result stays tileable."""
    h, w = height.shape
    fy = np.fft.fftfreq(h)[:, None]
    fx = np.fft.fftfreq(w)[None, :]
    kernel = np.exp(-2 * (np.pi * sigma) ** 2 * (fx * fx + fy * fy))
    return np.real(np.fft.ifft2(np.fft.fft2(height) * kernel))


def main():
    raw = pathlib.Path(sys.argv[1] if len(sys.argv) > 1 else ROOT / 'Artifacts/Local/Textures/raw')
    OUT.mkdir(parents=True, exist_ok=True)
    detail_atlas = np.zeros((2 * SIZE, 3 * SIZE, 3), np.float32)
    normal_atlas = np.zeros((2 * SIZE, 3 * SIZE, 3), np.float32)
    for index, name in enumerate(LAYERS):
        source = np.asarray(Image.open(raw / (name + '.png')).convert('RGB'), np.float32) / 255
        band = source.shape[0] // 12
        seamless = np.clip(crossfade_seamless(source, band), 0, 1)
        image = np.asarray(Image.fromarray((seamless * 255 + .5).astype(np.uint8)).resize((SIZE, SIZE), Image.LANCZOS), np.float32) / 255
        linear = srgb_to_linear(image)
        mean = linear.reshape(-1, 3).mean(axis=0)
        # Remove very low-frequency lighting/colour drift so tiles do not show as patches.
        drift = np.stack([blur_wrap(linear[..., c], SIZE / 6) for c in range(3)], -1)
        flattened = linear / np.maximum(drift, 1e-4) * mean
        # Luminance ratio carries the structure; hue deviation is kept but square-root
        # compressed, so dim channels (blue in grass) do not amplify into noise.
        weights = np.array([.2126, .7152, .0722], np.float32)
        mean_luminance = float(mean @ weights)
        ratio = (flattened @ weights) / mean_luminance
        hue = (flattened / np.maximum(flattened @ weights, 1e-4)[..., None]) / (mean / mean_luminance)
        detail = np.clip(ratio[..., None] * np.sqrt(np.maximum(hue, 0)) * .4, 0, 1)
        # Height from luminance, high-passed; normals by central differences with wrap.
        luminance = flattened @ np.array([.2126, .7152, .0722], np.float32)
        height = luminance - blur_wrap(luminance, 18)
        height /= max(1e-5, height.std())
        dx = (np.roll(height, -1, 1) - np.roll(height, 1, 1)) * .5
        dy = (np.roll(height, -1, 0) - np.roll(height, 1, 0)) * .5
        strength = BUMP[name] * .35
        normal = np.stack([-dx * strength, dy * strength, np.ones_like(dx)], -1)
        normal /= np.linalg.norm(normal, axis=-1, keepdims=True)
        row, column = divmod(index, 3)
        detail_atlas[row * SIZE:(row + 1) * SIZE, column * SIZE:(column + 1) * SIZE] = detail
        normal_atlas[row * SIZE:(row + 1) * SIZE, column * SIZE:(column + 1) * SIZE] = normal * .5 + .5
        print('%-9s mean linear %s  detail mean %.3f  std %.3f' % (name, np.round(mean, 4), detail.mean(), detail.std()))
    Image.fromarray((detail_atlas * 255 + .5).astype(np.uint8)).save(OUT / 'GroundDetail.png')
    Image.fromarray((normal_atlas * 255 + .5).astype(np.uint8)).save(OUT / 'GroundNormal.png')
    print('wrote', OUT / 'GroundDetail.png', OUT / 'GroundNormal.png')


if __name__ == '__main__':
    main()
