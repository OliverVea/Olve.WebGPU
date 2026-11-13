import { dotnet } from './_framework/dotnet.js';

const { runMain, getAssemblyExports, getConfig } = await dotnet.create();
const exports = await getAssemblyExports(getConfig().mainAssemblyName);
await runMain();

// document.title lets a headless browser see that frames are being drawn
let frames = 0;
function loop() {
    if (exports.Entry.Frame()) {
        document.title = 'frames ' + ++frames;
    }
    requestAnimationFrame(loop);
}
requestAnimationFrame(loop);
