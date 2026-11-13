"""Serves a published wasm app and checks it in headless Chromium with WebGPU.
usage: verify-web.py <wwwroot dir> tests|triangle [--channel chrome] [--headed] [--screenshot out.png]
  tests:    tests/Olve.WebGPU.Tests.Browser; passes when the page title becomes PASS
  triangle: samples/Triangle.Web; passes when frames are drawn and the canvas shows the triangle over the clear colour
needs: pip install playwright pillow && playwright install chromium"""
import argparse
import functools
import http.server
import io
import sys
import threading

from PIL import Image
from playwright.sync_api import sync_playwright

parser = argparse.ArgumentParser()
parser.add_argument("root")
parser.add_argument("mode", choices=["tests", "triangle"])
parser.add_argument("--channel")
parser.add_argument("--headed", action="store_true")
parser.add_argument("--screenshot")
args = parser.parse_args()

class QuietHandler(http.server.SimpleHTTPRequestHandler):
    def log_message(self, *_):
        pass


handler = functools.partial(QuietHandler, directory=args.root)
server = http.server.ThreadingHTTPServer(("127.0.0.1", 0), handler)
threading.Thread(target=server.serve_forever, daemon=True).start()
url = f"http://127.0.0.1:{server.server_address[1]}/index.html"

# Linux runners have no GPU: WebGPU on SwiftShader's Vulkan
flags = ["--enable-unsafe-webgpu", "--enable-unsafe-swiftshader", "--enable-features=Vulkan", "--use-vulkan=swiftshader",
         "--use-webgpu-adapter=swiftshader", "--use-angle=swiftshader", "--ignore-gpu-blocklist"]
if sys.platform == "win32":
    flags = ["--enable-unsafe-webgpu", "--use-angle=d3d11"]

ok = False
with sync_playwright() as p:
    browser = p.chromium.launch(channel=args.channel, headless=not args.headed, args=flags)
    page = browser.new_page(viewport={"width": 700, "height": 560})
    page.on("console", lambda m: print(f"console.{m.type}: {m.text}"))
    page.on("pageerror", lambda e: print(f"pageerror: {e}"))
    page.goto(url)
    print("adapter:", page.evaluate("""async () => {
        if (!navigator.gpu) return 'no navigator.gpu';
        const adapter = await navigator.gpu.requestAdapter();
        if (!adapter) return 'requestAdapter() gave null';
        const info = adapter.info ?? {};
        return `${info.vendor} ${info.architecture} ${info.description}`;
    }"""))

    try:
        if args.mode == "tests":
            page.wait_for_function("document.title === 'PASS' || document.title === 'FAIL'", timeout=180_000)
        else:
            page.wait_for_function("document.title.startsWith('frames ') && parseInt(document.title.slice(7)) > 60", timeout=180_000)
    except Exception as e:
        print("timed out:", e)
    title = page.title()
    print("title:", title)

    if args.mode == "tests":
        ok = title == "PASS"
    elif title.startswith("frames "):
        png = page.locator("#canvas").screenshot(path=args.screenshot)
        img = Image.open(io.BytesIO(png)).convert("RGB")
        w, h = img.size
        corner = img.getpixel((5, 5))
        centre = img.getpixel((w // 2, int(h * 0.6)))
        print(f"corner {corner}, centre {centre}")
        # Clear colour (0.1, 0.1, 0.15) at the corner; inside the triangle, a blend of red, green and blue
        ok = all(abs(a - b) <= 2 for a, b in zip(corner, (26, 26, 38))) and sum(centre) > 3 * 60
    browser.close()

print("PASS" if ok else "FAIL")
sys.exit(0 if ok else 1)
