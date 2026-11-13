"""Makes webgpu.h's functions visible in Dawn's Apple static libraries, in place.

Dawn's xcframework compiles them with hidden visibility (private external), which suits a Swift or Objective-C app that calls them
directly, but an app can't export a hidden symbol, so the binding's run-time lookup (dlsym in the app's own executable) can't find
them. This clears N_PEXT on exactly the functions in the list, in every Mach-O object of the archive, of every architecture of a
fat file. Nothing else changes and no size changes: members aren't extracted and repacked (an archive can hold several members
with the same name), and the archive's symbol index (__.SYMDEF) stays valid, since -force_load doesn't read it anyway.

usage: unhide-symbols.py <libwebgpu_dawn.a> <exports.exp>    (the .exp list from mkdef.py: one _name per line)
"""
import struct
import sys

N_STAB, N_PEXT, N_TYPE, N_EXT, N_SECT = 0xE0, 0x10, 0x0E, 0x01, 0x0E
LC_SYMTAB = 0x2


def patch_object(data, start, end, names):
    """Clears N_PEXT on the listed defined external symbols of the 64-bit Mach-O object in data[start:end]; returns how many."""
    magic, = struct.unpack_from('<I', data, start)
    if magic != 0xFEEDFACF:
        return 0
    ncmds, = struct.unpack_from('<I', data, start + 16)
    offset = start + 32
    patched = 0
    for _ in range(ncmds):
        cmd, size = struct.unpack_from('<II', data, offset)
        if cmd == LC_SYMTAB:
            symoff, nsyms, stroff, _ = struct.unpack_from('<IIII', data, offset + 8)
            for i in range(nsyms):
                entry = start + symoff + 16 * i
                strx, ntype = struct.unpack_from('<IB', data, entry)
                if ntype & N_STAB or ntype & N_TYPE != N_SECT or not ntype & N_EXT or not ntype & N_PEXT:
                    continue
                name_start = start + stroff + strx
                name = bytes(data[name_start:data.index(b'\0', name_start)])
                if name in names:
                    data[entry + 4] = ntype & ~N_PEXT
                    patched += 1
        offset += size
    assert offset <= end
    return patched


def patch_archive(data, start, end, names):
    """Patches every member of the BSD archive in data[start:end]."""
    assert bytes(data[start:start + 8]) == b'!<arch>\n', 'not an archive'
    offset = start + 8
    patched = 0
    while offset < end:
        name = bytes(data[offset:offset + 16]).rstrip()
        size = int(bytes(data[offset + 48:offset + 58]).strip())
        member = offset + 60
        # BSD long names: "#1/<length>", the name stored at the start of the member and counted in its size
        header_name = int(name[3:]) if name.startswith(b'#1/') else 0
        patched += patch_object(data, member + header_name, member + size, names)
        offset = member + size + (size & 1)
    return patched


def main():
    library, exports = sys.argv[1], sys.argv[2]
    names = {line.strip().encode() for line in open(exports, encoding='utf-8') if line.strip()}
    data = bytearray(open(library, 'rb').read())
    magic, = struct.unpack_from('>I', data, 0)
    if magic in (0xCAFEBABE, 0xCAFEBABF):
        count, = struct.unpack_from('>I', data, 4)
        wide = magic == 0xCAFEBABF
        slices = []
        for i in range(count):
            if wide:
                offset, size = struct.unpack_from('>QQ', data, 8 + 32 * i + 8)
            else:
                offset, size = struct.unpack_from('>II', data, 8 + 20 * i + 8)
            slices.append((offset, offset + size))
    else:
        slices = [(0, len(data))]

    for start, end in slices:
        patched = patch_archive(data, start, end, names)
        print(f'{library}: {patched} of {len(names)} functions made visible in the slice at {start}')
        if patched != len(names):
            sys.exit(f'expected to find each of the {len(names)} functions once, found {patched}')

    open(library, 'wb').write(data)


main()
