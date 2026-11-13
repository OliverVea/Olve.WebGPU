import { dotnet } from './_framework/dotnet.js';

const { runMain } = await dotnet.create();
try {
    const exitCode = await runMain();
    document.title = exitCode === 0 ? 'PASS' : 'FAIL';
} catch (e) {
    console.error(e);
    document.title = 'FAIL';
}
