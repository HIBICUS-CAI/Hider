# libhidercap.so: PipeWire helper for the Linux ScreenCast grabber (design §5.3, Step 8).
#
#   make RID=linux-x64      -> out/linux-x64/libhidercap.so
#   make RID=linux-arm64    -> out/linux-arm64/libhidercap.so
#
# RID defaults to the host. Needs libpipewire-0.3-dev (pkg-config). Building for the other architecture needs a cross
# compiler (CC=aarch64-linux-gnu-gcc) and that architecture's libpipewire-0.3-dev (PKG_CONFIG_PATH pointing at it).

HOST_ARCH := $(shell uname -m)
ifeq ($(HOST_ARCH),aarch64)
  HOST_RID := linux-arm64
else
  HOST_RID := linux-x64
endif
RID ?= $(HOST_RID)

ifneq ($(RID),$(HOST_RID))
  ifeq ($(RID),linux-arm64)
    CC := aarch64-linux-gnu-gcc
  else ifeq ($(RID),linux-x64)
    CC := x86_64-linux-gnu-gcc
  else
    $(error Unsupported RID: $(RID))
  endif
endif

OUT_DIR := out/$(RID)
TARGET  := $(OUT_DIR)/libhidercap.so
SRCS    := hidercap.c

CFLAGS  ?= -O2
PKG_CONFIG ?= pkg-config
CFLAGS  += -std=c11 -Wall -Wextra -Werror -fPIC -fvisibility=hidden $(shell $(PKG_CONFIG) --cflags libpipewire-0.3)
LDFLAGS += -shared -Wl,--no-undefined -Wl,-soname,libhidercap.so
LDLIBS  += $(shell $(PKG_CONFIG) --libs libpipewire-0.3)

.PHONY: all clean
all: $(TARGET)

$(TARGET): $(SRCS) hidercap.h Makefile
	@mkdir -p $(OUT_DIR)
	$(CC) $(CFLAGS) $(SRCS) -o $@ $(LDFLAGS) $(LDLIBS)

clean:
	rm -rf out
