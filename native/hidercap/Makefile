# libhidercap.so: PipeWire helper for the Linux ScreenCast grabber (design §5.3, Step 8).
#
#   make RID=linux-x64      -> out/linux-x64/libhidercap.so
#   make RID=linux-arm64    -> out/linux-arm64/libhidercap.so
#
# RID defaults to the host. Needs libpipewire-0.3-dev (pkg-config). Building for the other architecture needs a cross
# compiler (picked automatically unless CC is given) and that architecture's libpipewire-0.3-dev; point pkg-config at
# it with PKG_CONFIG_LIBDIR (its .pc directory) and PKG_CONFIG_SYSROOT_DIR, or set CROSS_COMPILE / PKG_CONFIG.
# CFLAGS, CPPFLAGS and LDFLAGS from the command line or the environment are added to the required flags.

HOST_ARCH := $(shell uname -m)
ifeq ($(HOST_ARCH),aarch64)
  HOST_RID := linux-arm64
else ifeq ($(HOST_ARCH),x86_64)
  HOST_RID := linux-x64
else
  $(error Unsupported host architecture: $(HOST_ARCH) (linux-x64 and linux-arm64 only))
endif
RID ?= $(HOST_RID)

ifeq ($(RID),linux-arm64)
  TRIPLET := aarch64-linux-gnu
else ifeq ($(RID),linux-x64)
  TRIPLET := x86_64-linux-gnu
else
  $(error Unsupported RID: $(RID))
endif
ifneq ($(RID),$(HOST_RID))
  CROSS_COMPILE ?= $(TRIPLET)-
  # Only make's built-in default is replaced: a CC given by the user (clang --target=..., another gcc) is kept.
  ifeq ($(origin CC),default)
    CC := $(CROSS_COMPILE)gcc
  endif
endif

OUT_DIR := out/$(RID)
TARGET  := $(OUT_DIR)/libhidercap.so
SRCS    := hidercap.c

PKG_CONFIG ?= $(CROSS_COMPILE)pkg-config
ifneq ($(MAKECMDGOALS),clean)
  ifneq ($(shell $(PKG_CONFIG) --exists libpipewire-0.3 && echo yes),yes)
    $(error libpipewire-0.3 not found by $(PKG_CONFIG): install libpipewire-0.3-dev (for $(RID)))
  endif
endif
PW_CFLAGS := $(shell $(PKG_CONFIG) --cflags libpipewire-0.3)
PW_LIBS   := $(shell $(PKG_CONFIG) --libs libpipewire-0.3)

CFLAGS ?= -O2
# Required whatever CFLAGS / LDFLAGS say: the language level, warnings as errors, a position independent shared object
# exporting only the API, and hardening (not left to the compiler's defaults, which differ between distributions).
HIDERCAP_CFLAGS  := -std=c11 -Wall -Wextra -Werror -fPIC -fvisibility=hidden -fstack-protector-strong \
                    -fstack-clash-protection $(PW_CFLAGS)
HIDERCAP_CPPFLAGS := -U_FORTIFY_SOURCE -D_FORTIFY_SOURCE=3
HIDERCAP_LDFLAGS := -shared -Wl,--no-undefined -Wl,-soname,libhidercap.so -Wl,-z,relro,-z,now,-z,noexecstack

.PHONY: all clean
.DELETE_ON_ERROR:
all: $(TARGET)

$(TARGET): $(SRCS) hidercap.h Makefile
	@mkdir -p $(OUT_DIR)
	$(CC) $(HIDERCAP_CPPFLAGS) $(CPPFLAGS) $(HIDERCAP_CFLAGS) $(CFLAGS) $(SRCS) -o $@ $(HIDERCAP_LDFLAGS) $(LDFLAGS) $(PW_LIBS) $(LDLIBS)

clean:
	rm -rf out
