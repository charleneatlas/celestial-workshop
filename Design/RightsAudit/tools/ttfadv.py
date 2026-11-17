import struct
def advances(path, pt=90.0):
    d = open(path, 'rb').read()
    n = struct.unpack('>H', d[4:6])[0]
    t = {d[12+16*i:16+16*i]: struct.unpack('>II', d[20+16*i:28+16*i]) for i in range(n)}
    upm = struct.unpack('>H', d[t[b'head'][0]+18:t[b'head'][0]+20])[0]
    nh = struct.unpack('>H', d[t[b'hhea'][0]+34:t[b'hhea'][0]+36])[0]
    ho = t[b'hmtx'][0]
    hm = [struct.unpack('>H', d[ho+4*i:ho+4*i+2])[0] for i in range(nh)]
    off = t[b'cmap'][0]; nt = struct.unpack('>H', d[off+2:off+4])[0]; cmap = {}
    for i in range(nt):
        pid, eid, so = struct.unpack('>HHI', d[off+4+8*i:off+12+8*i]); s = off+so
        if struct.unpack('>H', d[s:s+2])[0] != 4: continue
        segx2 = struct.unpack('>H', d[s+6:s+8])[0]; seg = segx2//2
        ends = struct.unpack('>%dH'%seg, d[s+14:s+14+segx2]); starts = struct.unpack('>%dH'%seg, d[s+16+segx2:s+16+2*segx2])
        deltas = struct.unpack('>%dh'%seg, d[s+16+2*segx2:s+16+3*segx2]); ro = s+16+3*segx2
        ranges = struct.unpack('>%dH'%seg, d[ro:ro+segx2])
        for k,(a,b) in enumerate(zip(starts, ends)):
            for c in range(a, b+1):
                if c == 0xFFFF: continue
                if ranges[k] == 0: g = (c + deltas[k]) & 0xFFFF
                else:
                    p = ro + 2*k + ranges[k] + 2*(c-a); g = struct.unpack('>H', d[p:p+2])[0]
                    if g: g = (g + deltas[k]) & 0xFFFF
                cmap[c] = g
    return {c: hm[min(g, nh-1)] / upm * pt for c, g in cmap.items()}
