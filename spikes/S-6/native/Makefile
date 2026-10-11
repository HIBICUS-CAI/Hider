# S-6 prototype of libhidercap.so. Needs gcc and libpipewire-0.3-dev.
CFLAGS ?= -O2
CFLAGS += -std=c11 -Wall -Wextra -Werror -fPIC -fvisibility=hidden $(shell pkg-config --cflags libpipewire-0.3)
LDLIBS += $(shell pkg-config --libs libpipewire-0.3)

out/libhidercap.so: hidercap.c hidercap.h Makefile
	@mkdir -p out
	$(CC) $(CFLAGS) hidercap.c -o $@ -shared -Wl,--no-undefined -Wl,-soname,libhidercap.so $(LDLIBS)

clean:
	rm -rf out
