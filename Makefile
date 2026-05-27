TAG ?= local

.PHONY: build publish package release clean

build:
	powershell -NoProfile -ExecutionPolicy Bypass -File scripts/release.ps1 -Task build

publish:
	powershell -NoProfile -ExecutionPolicy Bypass -File scripts/release.ps1 -Task publish

package:
	powershell -NoProfile -ExecutionPolicy Bypass -File scripts/release.ps1 -Task package -Tag $(TAG)

release:
	powershell -NoProfile -ExecutionPolicy Bypass -File scripts/release.ps1 -Task release -Tag $(TAG)

clean:
	powershell -NoProfile -ExecutionPolicy Bypass -File scripts/release.ps1 -Task clean
