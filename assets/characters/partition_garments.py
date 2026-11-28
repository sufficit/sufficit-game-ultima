"""Partition the Innerscene skin into fitted garment primitives (no new vertices)."""
import json
from pathlib import Path
import struct
import sys


def partition(path):
    glb = path.read_bytes()
    _, _, _, size, _ = struct.unpack_from("<4sIIII", glb)
    doc = json.loads(glb[20:20 + size])
    binary = bytearray(glb[28 + size:])
    template, = doc["meshes"][0]["primitives"]

    def read(index, format):
        accessor = doc["accessors"][index]
        view = doc["bufferViews"][accessor["bufferView"]]
        offset = view.get("byteOffset", 0) + accessor.get("byteOffset", 0)
        size = struct.calcsize(format)
        return list(struct.iter_unpack(format, binary[offset:offset + size * accessor["count"]]))

    vertices = read(template["attributes"]["POSITION"], "<fff")
    index_type = doc["accessors"][template["indices"]]["componentType"]
    format = {5123: "H", 5125: "I"}[index_type]
    indices = [value for (value,) in read(template["indices"], "<" + format)]
    materials = ["avatar-skin", "outfit-shirt", "outfit-trousers", "outfit-boots"]
    groups = [[] for _ in materials]
    for start in range(0, len(indices), 3):
        triangle = indices[start:start + 3]
        x, y, _ = (sum(axis) / 3 for axis in zip(*(vertices[i] for i in triangle)))
        if y < .115:
            group = 3
        elif y < .94 and abs(x) < .23:
            group = 2
        elif y < 1.43 and y >= .90 and (abs(x) < .27 or y < 1.36 and abs(x) < .40):
            group = 1
        else:
            group = 0
        groups[group].extend(triangle)

    doc["meshes"][0]["primitives"] = []
    for material, (name, group) in enumerate(zip(materials, groups)):
        binary.extend(b"\0" * (-len(binary) % 4))
        offset = len(binary)
        data = struct.pack("<" + str(len(group)) + format, *group)
        binary.extend(data)
        view = len(doc["bufferViews"])
        doc["bufferViews"].append(dict(buffer=0, byteOffset=offset, byteLength=len(data), target=34963))
        accessor = len(doc["accessors"])
        doc["accessors"].append(dict(bufferView=view, componentType=index_type, count=len(group), type="SCALAR"))
        doc["meshes"][0]["primitives"].append({**template, "indices": accessor, "material": material})
        if material:
            doc["materials"].append(dict(name=name, pbrMetallicRoughness=dict(metallicFactor=0, roughnessFactor=.88)))
        else:
            doc["materials"][0]["name"] = name
            doc["materials"][0]["pbrMetallicRoughness"]["roughnessFactor"] = .72
    doc["buffers"][0]["byteLength"] = len(binary)
    metadata = json.dumps(doc, separators=(",", ":")).encode()
    metadata += b" " * (-len(metadata) % 4)
    binary.extend(b"\0" * (-len(binary) % 4))
    path.write_bytes(struct.pack("<4sIIII", b"glTF", 2, 28 + len(metadata) + len(binary), len(metadata), 0x4e4f534a)
                     + metadata + struct.pack("<II", len(binary), 0x004e4942) + binary)

if __name__ == "__main__":
    partition(Path(sys.argv[1]))
