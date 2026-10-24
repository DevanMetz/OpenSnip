#!/bin/sh
set -eu
here=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
install -Dm755 "$here/OpenSnip" "$HOME/.local/bin/opensnip"
install -Dm644 "$here/opensnip.desktop" "$HOME/.local/share/applications/opensnip.desktop"
install -Dm644 "$here/opensnip.png" "$HOME/.local/share/icons/hicolor/256x256/apps/opensnip.png"
