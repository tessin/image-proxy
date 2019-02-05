# Tessin Image Exchange (TIX)

Image proxy and edge cache.

## API

To request an image you make a GET request passing the source image as a query string parameter `s`.

For example,

> `https://tessin-cdn.azureedge.net/api/tix?q=50&s=https%3A%2F%2Ftessin.se%2Fstatic%2Fimages%2Fhero.091dc42f.jpg&w=640`

The image proxy will request the image `https://tessin.se/static/images/hero.091dc42f.jpg` and encode the response using JPEG quality level 50 and max width 640 (keeping original aspect ratio).

> **IMPORTANT!:** use a canonical resource representation (canonical URLs) or you will get CDN cache misses. For example, use a stable sort order for request query string parameters to prevent otherwise ideitical URLs from having different representations.

### OPTIONS

| parameter  | required | type  | range                | default     | description                                                                                                  |
| ---------- | -------- | ----- | -------------------- | ----------- | ------------------------------------------------------------------------------------------------------------ |
| `s`        | yes      | URI   | absolute             | -           | absolute URI for source image, any source supported by the Windows Imaging Component (WIC) is supported here |
| `c`        | no       | rect  | x,y,w,h              | 0,0,100,100 | crop rectangle in percent                                                                                    |
| `w`        | no       | int   | >=0                  | 0           | desired pixel width, see remarks                                                                             |
| `h`        | no       | int   | >=0                  | 0           | desired pixel height, see remarks                                                                            |
| `q`        | no       | int   | 0-100                | 80          | desired image quality level                                                                                  |
| `t`        | no       | enum  | `jpg`, `webp`        | `jpg`       | desired image format                                                                                         |
| `f`        | no       | enum  | `none`, `min`, `max` | `max`       | fitting strategy (when `w>=0&h>=0`), see remarks                                                             |
| `bg-color` | no       | color | #hhh, #hhhhhh        | black       | background color (when `f=min`), see remarks                                                                 |

#### REMARKS

When only pixel width or pixel height is specified the image is resized with respect to the original aspect ratio. When both pixel width and pixel height the result depends on the `f` option. `none` will ignore the aspect ratio and just scale the image to fit the desired dimensions. `min` will scale the image to fit inside the desired dimensions. This will result in a border around the image. `max` (default) will scale the image to fit outside the desired dimensions this will result in the image covering the desired dimensions but parts of the image may be cropped.

Cropping is applied before any other processing and is specified in percent. To crop the top left corner of the image you say `0,0,25,25` to crop the bottom right corner of the image you say `75,75,25,25`. The crop rectange is always in percentages of source image dimensions and cannot exceed 100% in any one dimension. Formally the crop rectange must respect the invariant `x<=100,y<=100,x+w<=100,y+h<=100`.

## JavaScript API

[image-proxy.js]

```
// @flow

type ImageProxyOptions = {
  cropRect: number[],
  width?: number,
  height?: number,
  quality?: number,
  type?: 'jpg' | 'webp',
  stretch?: 'none' | 'min' | 'max',
  bgColor?: string
}

export default function getImageProxyUrl(source: string, opts: ImageProxyOptions): string {
  if (!source) throw new TypeError('source is required')

  opts = opts || {}

  var s = 'https://tessin-cdn.azureedge.net/api/tix?s=' + encodeURIComponent(source)

  if (opts.cropRect) {
    s += '&c=' + opts.cropRect.join(',')
  }

  if (opts.width) {
    s += '&w=' + opts.width
  }

  if (opts.height) {
    s += '&h=' + opts.height
  }

  if (opts.quality) {
    s += '&q=' + opts.quality
  }

  if (opts.type) {
    s += '&t=' + opts.type
  }

  if (opts.stretch) {
    s += '&f=' + opts.stretch
  }

  if (opts.bgColor) {
    s += '&bg-color=' + encodeURIComponent(opts.bgColor) // escape #
  }

  return s
}
```

## libwebp

You can get the `libwebp` precompiled binaries [here](https://storage.googleapis.com/downloads.webmproject.org/releases/webp/index.html).
