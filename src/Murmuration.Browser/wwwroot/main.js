// Static hosts cache unfingerprinted files, so a stale one after a redeploy can mismatch the new fingerprinted assets.
// Revalidating them first refreshes the HTTP cache before the runtime imports them.
const unfingerprinted = ['./_framework/dotnet.js', './_framework/avalonia.js', './webgpu-renderer.js'];

await Promise.all(unfingerprinted.map(async url => {
    try {
        await fetch(new URL(url, import.meta.url), { cache: 'no-cache' });
    } catch {
        // Offline or blocked; let the normal load path report the real error.
    }
}));

const { dotnet } = await import('./_framework/dotnet.js');

const dotnetRuntime = await dotnet
    .withDiagnosticTracing(false)
    .withApplicationArgumentsFromQuery()
    .create();

const config = dotnetRuntime.getConfig();

await dotnetRuntime.runMain(config.mainAssemblyName, [globalThis.location.href]);
