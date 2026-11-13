"""Lists every wgpu* function webgpu.h exports, as the linker wants it for a shared library.
usage: mkdef.py <include/dawn/webgpu.h> <out>
  out ending in .def: a module-definition file (link.exe), .map: an ld version script, .exp: a macOS exported symbols list."""
import re
import sys

header, out = sys.argv[1], sys.argv[2]
names = sorted(set(re.findall(r'WGPU_EXPORT [^;]*? (wgpu\w+)\(', open(header, encoding='utf-8').read())))
with open(out, 'w', newline='\n') as f:
    if out.endswith('.map'):
        f.write('{ global:\n' + ''.join(f'    {n};\n' for n in names) + 'local: *; };\n')
    elif out.endswith('.exp'):
        f.write(''.join(f'_{n}\n' for n in names))
    else:
        f.write('LIBRARY webgpu_dawn\nEXPORTS\n' + ''.join(f'    {n}\n' for n in names))
print(len(names), 'exports')
