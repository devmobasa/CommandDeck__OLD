.PHONY: build test validate publish reinstall

build:
	./scripts/build.sh

test:
	./scripts/test.sh

validate:
	./scripts/validate-repository.sh

publish:
	./scripts/publish-aot.sh linux-x64

reinstall:
	./scripts/reinstall-all.sh
