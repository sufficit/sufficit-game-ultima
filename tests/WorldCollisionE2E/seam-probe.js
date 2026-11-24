// Captura só os texels que participam da filtragem nas divisas.
(points) => {
    const scene = BABYLON.EngineStore.LastCreatedScene;
    const center = scene.getMeshByName('ground-0,0');
    const east = scene.getMeshByName('ground-1,0');
    const north = scene.getMeshByName('ground-0,1');

    function strip(mesh, alongX, edge) {
        const tex = mesh.material.diffuseTexture;
        const { width, height } = tex.getSize();
        const { uScale, vScale, uOffset, vOffset } = tex;
        const size = alongX ? width : height;
        const coord = Math.floor(Math.max(0, Math.min(size - 1,
            (edge * (alongX ? uScale : vScale) + (alongX ? uOffset : vOffset)) * size - 0.5)));
        const span = Math.min(2, size - coord);
        const x = alongX ? coord : 0;
        const y = alongX ? 0 : coord;
        const stride = alongX ? span : width;
        const data = tex.getContext().getImageData(x, y, stride, alongX ? height : span).data;
        const pixels = btoa(String.fromCharCode(...data));
        return { width, height, uScale, vScale, uOffset, vOffset, x, y, stride, pixels };
    }

    function geometry(mesh, alongX) {
        const positions = mesh.getVerticesData(BABYLON.VertexBuffer.PositionKind);
        const normals = mesh.getVerticesData(BABYLON.VertexBuffer.NormalKind);
        const edge = new Map();
        for (let i = 0; i < positions.length; i += 3) {
            if (positions[i + (alongX ? 0 : 2)] !== 16) continue;
            edge.set(positions[i + (alongX ? 2 : 0)], [positions[i + 1], ...normals.slice(i, i + 3)]);
        }
        return Array.from(edge, ([location, values]) => ({ location, values }));
    }

    function boundary(neighbor, alongX) {
        return {
            center: strip(center, alongX, 1), neighbor: strip(neighbor, alongX, 0),
            centerEdge: geometry(center, alongX), neighborEdge: geometry(neighbor, alongX)
        };
    }

    const down = new BABYLON.Vector3(0, -1, 0);
    const wall = points.map(([x, z]) => {
        const ray = new BABYLON.Ray(new BABYLON.Vector3(x, 100, z), down);
        const visible = scene.pickWithRay(ray, mesh => mesh.name.startsWith('ground-')).pickedPoint.y;
        return { visible, simulated: game.heightAt(x, z) };
    });
    return { east: boundary(east, true), north: boundary(north, false), vertices: center.getTotalVertices(), wall };
}
