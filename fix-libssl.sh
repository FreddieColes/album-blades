#!/usr/bin/env bash
# Unity 2021's bundled .NET compiler needs libssl1.1, which Debian bookworm no longer ships.
if ldconfig -p | grep -q "libssl.so.1.1"; then echo "libssl1.1 already installed"; exit 0; fi
echo "Installing libssl1.1 for Unity's compiler..."
POOL=http://deb.debian.org/debian/pool/main/o/openssl/
DEB=$(curl -fsSL "$POOL" | grep -oE 'libssl1\.1_1\.1\.1[^"]*_amd64\.deb' | sort -uV | tail -1)
[ -z "$DEB" ] && DEB=libssl1.1_1.1.1w-0+deb11u1_amd64.deb
echo "Using $DEB"
curl -fsSL -o /tmp/libssl1.1.deb "$POOL$DEB" && sudo dpkg -i /tmp/libssl1.1.deb && rm -f /tmp/libssl1.1.deb
ldconfig -p | grep -q "libssl.so.1.1" && echo "libssl1.1 installed" || { echo "libssl1.1 install FAILED"; exit 1; }
