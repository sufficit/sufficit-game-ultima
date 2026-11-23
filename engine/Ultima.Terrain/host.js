const version = new URL(import.meta.url).search;
export const ready = import('./_framework/dotnet.js' + version).then(async ({ dotnet }) => {
    const runtime = await dotnet.create();
    const exports = await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName);
    return exports.Ultima.Terrain.BrowserExports;
});
