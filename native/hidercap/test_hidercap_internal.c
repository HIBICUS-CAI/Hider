/* Unit test of hidercap.c's internals without a PipeWire daemon: which buffers are usable, what the stream callbacks do
 * with buffers and formats, the pixel copy and a grab of a held frame, and the clean-up after each step of hidercap_start that can fail.
 *
 * hidercap.c is included, so its static functions and its state (g) are reachable. The PipeWire calls they make are
 * wrapped at link time (-Wl,--wrap=..., see the Makefile): a wrapper acts on the fake stream / loop below, or fails the
 * start step that fail_at names, and otherwise forwards to libpipewire (__real_*). Run by "make test". */
#include "hidercap.c"

#include <errno.h>
#include <fcntl.h>
#include <poll.h>
#include <stdio.h>
#include <sys/socket.h>

#include <spa/param/audio/format-utils.h>

static int failures;

#define CHECK(_cond) \
    do { \
        if (!(_cond)) { \
            fprintf(stderr, "%s:%d: CHECK(%s) failed\n", __FILE__, __LINE__, #_cond); \
            failures++; \
        } \
    } while (0)

/* ---- Link-time wrappers ------------------------------------------------------------------------------------------ */

/* Stand-ins for the stream and the loop of the callback tests: never dereferenced, only compared. */
static char fake_stream_storage, fake_loop_storage;
#define FAKE_STREAM ((struct pw_stream *)(void *)&fake_stream_storage)
#define FAKE_LOOP ((struct pw_thread_loop *)(void *)&fake_loop_storage)

/* What pw_stream_dequeue_buffer hands out on the fake stream, in order (then NULL). */
static struct pw_buffer *script[8];
static int script_len, script_pos;
/* What was queued back on the fake stream, in order. */
static struct pw_buffer *queued[16];
static int queued_len;
static int signals;

/* The last pw_stream_update_params on the fake stream, decoded while its pods were alive. */
static struct {
    int calls;
    uint32_t n_params;
    uint32_t types[2], ids[2];
    int32_t buffers_default, buffers_min, buffers_max;
    int32_t data_types;
    uint32_t meta_type;
    int32_t meta_size;
} update;

enum start_step { FAIL_NONE, FAIL_LOOP_NEW, FAIL_CONTEXT_NEW, FAIL_LOOP_START, FAIL_STREAM_NEW, FAIL_CONNECT };
static enum start_step fail_at;

struct pw_buffer *__real_pw_stream_dequeue_buffer(struct pw_stream *_stream);
int __real_pw_stream_queue_buffer(struct pw_stream *_stream, struct pw_buffer *_buffer);
void __real_pw_thread_loop_signal(struct pw_thread_loop *_loop, bool _wait_for_accept);
int __real_pw_stream_update_params(struct pw_stream *_stream, const struct spa_pod **_params, uint32_t _n_params);
struct pw_thread_loop *__real_pw_thread_loop_new(const char *_name, const struct spa_dict *_props);
struct pw_context *__real_pw_context_new(struct pw_loop *_main_loop, struct pw_properties *_props, size_t _user_data_size);
int __real_pw_thread_loop_start(struct pw_thread_loop *_loop);
struct pw_stream *__real_pw_stream_new(struct pw_core *_core, const char *_name, struct pw_properties *_props);
int __real_pw_stream_connect(struct pw_stream *_stream, enum pw_direction _direction, uint32_t _target_id,
    enum pw_stream_flags _flags, const struct spa_pod **_params, uint32_t _n_params);

struct pw_buffer *__wrap_pw_stream_dequeue_buffer(struct pw_stream *_stream);
int __wrap_pw_stream_queue_buffer(struct pw_stream *_stream, struct pw_buffer *_buffer);
void __wrap_pw_thread_loop_signal(struct pw_thread_loop *_loop, bool _wait_for_accept);
int __wrap_pw_stream_update_params(struct pw_stream *_stream, const struct spa_pod **_params, uint32_t _n_params);
struct pw_thread_loop *__wrap_pw_thread_loop_new(const char *_name, const struct spa_dict *_props);
struct pw_context *__wrap_pw_context_new(struct pw_loop *_main_loop, struct pw_properties *_props, size_t _user_data_size);
int __wrap_pw_thread_loop_start(struct pw_thread_loop *_loop);
struct pw_stream *__wrap_pw_stream_new(struct pw_core *_core, const char *_name, struct pw_properties *_props);
int __wrap_pw_stream_connect(struct pw_stream *_stream, enum pw_direction _direction, uint32_t _target_id,
    enum pw_stream_flags _flags, const struct spa_pod **_params, uint32_t _n_params);

struct pw_buffer *__wrap_pw_stream_dequeue_buffer(struct pw_stream *_stream)
{
    if (_stream != FAKE_STREAM) {
        return __real_pw_stream_dequeue_buffer(_stream);
    }
    return script_pos < script_len ? script[script_pos++] : NULL;
}

int __wrap_pw_stream_queue_buffer(struct pw_stream *_stream, struct pw_buffer *_buffer)
{
    if (_stream != FAKE_STREAM) {
        return __real_pw_stream_queue_buffer(_stream, _buffer);
    }
    if (queued_len < (int)(sizeof(queued) / sizeof(queued[0]))) {
        queued[queued_len++] = _buffer;
    }
    return 0;
}

void __wrap_pw_thread_loop_signal(struct pw_thread_loop *_loop, bool _wait_for_accept)
{
    if (_loop != FAKE_LOOP) {
        __real_pw_thread_loop_signal(_loop, _wait_for_accept);
        return;
    }
    CHECK(!_wait_for_accept);
    signals++;
}

int __wrap_pw_stream_update_params(struct pw_stream *_stream, const struct spa_pod **_params, uint32_t _n_params)
{
    if (_stream != FAKE_STREAM) {
        return __real_pw_stream_update_params(_stream, _params, _n_params);
    }
    memset(&update, 0, sizeof(update));
    update.calls = 1;
    update.n_params = _n_params;
    for (uint32_t i = 0; i < _n_params && i < 2; i++) {
        const struct spa_pod_object *o = (const struct spa_pod_object *)_params[i];
        update.types[i] = o->body.type;
        update.ids[i] = o->body.id;
    }
    if (_n_params == 2) {
        const struct spa_pod *buffers = NULL, *data_type = NULL;
        CHECK(spa_pod_parse_object(_params[0], SPA_TYPE_OBJECT_ParamBuffers, NULL,
                  SPA_PARAM_BUFFERS_buffers, SPA_POD_PodChoice(&buffers),
                  SPA_PARAM_BUFFERS_dataType, SPA_POD_PodChoice(&data_type)) == 2);
        uint32_t n = 0, choice = 0;
        if (buffers != NULL) {
            /* A range choice: default, min, max. */
            const struct spa_pod *values = spa_pod_get_values(buffers, &n, &choice);
            CHECK(choice == SPA_CHOICE_Range && n == 3 && values->type == SPA_TYPE_Int);
            const int32_t *v = (const int32_t *)SPA_POD_BODY_CONST(values);
            update.buffers_default = v[0];
            update.buffers_min = v[1];
            update.buffers_max = v[2];
        }
        if (data_type != NULL) {
            /* A flags choice: the accepted data types as one mask. */
            const struct spa_pod *values = spa_pod_get_values(data_type, &n, &choice);
            CHECK(choice == SPA_CHOICE_Flags && n == 1 && values->type == SPA_TYPE_Int);
            update.data_types = *(const int32_t *)SPA_POD_BODY_CONST(values);
        }
        CHECK(spa_pod_parse_object(_params[1], SPA_TYPE_OBJECT_ParamMeta, NULL,
                  SPA_PARAM_META_type, SPA_POD_Id(&update.meta_type),
                  SPA_PARAM_META_size, SPA_POD_Int(&update.meta_size)) == 2);
    }
    return 0;
}

struct pw_thread_loop *__wrap_pw_thread_loop_new(const char *_name, const struct spa_dict *_props)
{
    return fail_at == FAIL_LOOP_NEW ? NULL : __real_pw_thread_loop_new(_name, _props);
}

struct pw_context *__wrap_pw_context_new(struct pw_loop *_main_loop, struct pw_properties *_props, size_t _user_data_size)
{
    if (fail_at == FAIL_CONTEXT_NEW) {
        pw_properties_free(_props); /* pw_context_new takes the properties, also on failure */
        return NULL;
    }
    return __real_pw_context_new(_main_loop, _props, _user_data_size);
}

int __wrap_pw_thread_loop_start(struct pw_thread_loop *_loop)
{
    return fail_at == FAIL_LOOP_START ? -EIO : __real_pw_thread_loop_start(_loop);
}

struct pw_stream *__wrap_pw_stream_new(struct pw_core *_core, const char *_name, struct pw_properties *_props)
{
    if (fail_at == FAIL_STREAM_NEW) {
        pw_properties_free(_props); /* pw_stream_new takes the properties, also on failure */
        return NULL;
    }
    return __real_pw_stream_new(_core, _name, _props);
}

int __wrap_pw_stream_connect(struct pw_stream *_stream, enum pw_direction _direction, uint32_t _target_id,
    enum pw_stream_flags _flags, const struct spa_pod **_params, uint32_t _n_params)
{
    if (fail_at == FAIL_CONNECT) {
        return -EIO;
    }
    return __real_pw_stream_connect(_stream, _direction, _target_id, _flags, _params, _n_params);
}

/* ---- Fixtures ---------------------------------------------------------------------------------------------------- */

/* A capture buffer as a stream hands it out: one data block over mem, its chunk, and optionally a header meta. */
struct fake_buffer {
    struct pw_buffer pw;
    struct spa_buffer sb;
    struct spa_data data;
    struct spa_chunk chunk;
    struct spa_meta meta;
    struct spa_meta_header header;
    uint8_t mem[256];
};

/* A buffer that is usable for a 4x2 format: offset 0, 32 bytes, stride 16, no meta. */
static void init_buffer(struct fake_buffer *_b)
{
    memset(_b, 0, sizeof(*_b));
    _b->pw.buffer = &_b->sb;
    _b->sb.n_datas = 1;
    _b->sb.datas = &_b->data;
    _b->data.type = SPA_DATA_MemPtr;
    _b->data.data = _b->mem;
    _b->data.maxsize = 32;
    _b->data.chunk = &_b->chunk;
    _b->chunk.offset = 0;
    _b->chunk.size = 32;
    _b->chunk.stride = 16;
}

static void add_header(struct fake_buffer *_b, int64_t _pts, uint32_t _flags)
{
    _b->header.pts = _pts;
    _b->header.flags = _flags;
    _b->meta.type = SPA_META_Header;
    _b->meta.size = sizeof(_b->header);
    _b->meta.data = &_b->header;
    _b->sb.n_metas = 1;
    _b->sb.metas = &_b->meta;
}

/* g as a started stream would have it, on the fake stream and loop, with a 4x2 BGRx format. */
static void reset_state(void)
{
    memset(&g, 0, sizeof(g));
    g.stream = FAKE_STREAM;
    g.loop = FAKE_LOOP;
    g.format.format = SPA_VIDEO_FORMAT_BGRx;
    g.format.size = SPA_RECTANGLE(4, 2);
    g.has_format = 1;
    script_len = script_pos = 0;
    queued_len = 0;
    signals = 0;
    memset(&update, 0, sizeof(update));
}

/* Leaves g as hidercap_stop does, so that the fake pointers never reach libpipewire. */
static void clear_state(void)
{
    memset(&g, 0, sizeof(g));
}

static void set_script(int _n, struct pw_buffer **_buffers)
{
    for (int i = 0; i < _n; i++) {
        script[i] = _buffers[i];
    }
    script_len = _n;
    script_pos = 0;
}

/* ---- usable / chunk_stride --------------------------------------------------------------------------------------- */

static void test_usable(void)
{
    struct fake_buffer b;

    reset_state();
    init_buffer(&b);
    CHECK(usable(&b.pw) == 1);

    /* The data block must exist, be mapped, have a chunk with data that is not corrupted. */
    init_buffer(&b);
    b.sb.n_datas = 0;
    CHECK(usable(&b.pw) == 0);
    init_buffer(&b);
    b.data.data = NULL;
    CHECK(usable(&b.pw) == 0);
    init_buffer(&b);
    b.data.chunk = NULL;
    CHECK(usable(&b.pw) == 0);
    init_buffer(&b);
    b.chunk.size = 0;
    CHECK(usable(&b.pw) == 0);
    init_buffer(&b);
    b.chunk.flags = SPA_CHUNK_FLAG_CORRUPTED;
    CHECK(usable(&b.pw) == 0);
    init_buffer(&b);
    b.chunk.flags = SPA_CHUNK_FLAG_EMPTY; /* another flag does not matter */
    CHECK(usable(&b.pw) == 1);

    /* A format is needed, with a size. */
    init_buffer(&b);
    g.has_format = 0;
    CHECK(usable(&b.pw) == 0);
    reset_state();
    g.format.size = SPA_RECTANGLE(0, 2);
    CHECK(usable(&b.pw) == 0);
    g.format.size = SPA_RECTANGLE(4, 0);
    CHECK(usable(&b.pw) == 0);
    reset_state();

    /* The stride must hold a row (4 * 4 bytes); 0 means "the row" (chunk_stride). */
    init_buffer(&b);
    b.chunk.stride = 15;
    CHECK(usable(&b.pw) == 0);
    b.chunk.stride = -16;
    CHECK(usable(&b.pw) == 0);
    b.chunk.stride = 0;
    CHECK(chunk_stride(&b.data) == 16);
    CHECK(usable(&b.pw) == 1);
    b.chunk.stride = 20;
    CHECK(chunk_stride(&b.data) == 20);

    /* The last row must end inside maxsize: offset + stride * (height - 1) + row. */
    init_buffer(&b);
    b.data.maxsize = 31;
    CHECK(usable(&b.pw) == 0);
    b.chunk.stride = 20; /* 20 + 16 = 36 */
    b.data.maxsize = 35;
    CHECK(usable(&b.pw) == 0);
    b.data.maxsize = 36;
    CHECK(usable(&b.pw) == 1);
    b.chunk.offset = 4; /* 4 + 36 = 40 */
    CHECK(usable(&b.pw) == 0);
    b.data.maxsize = 40;
    CHECK(usable(&b.pw) == 1);

    /* No overflow on a huge format: the sum is 64-bit and lands past maxsize. */
    init_buffer(&b);
    g.format.size = SPA_RECTANGLE(4, 0x80000000u);
    b.chunk.stride = 0x7FFFFFFF;
    b.data.maxsize = 0xFFFFFFFFu;
    CHECK(usable(&b.pw) == 0);
    clear_state();
}

/* ---- on_process -------------------------------------------------------------------------------------------------- */

static int64_t monotonic_ns(void)
{
    struct timespec ts;
    clock_gettime(CLOCK_MONOTONIC, &ts);
    return (int64_t)ts.tv_sec * 1000000000LL + ts.tv_nsec;
}

static void test_process_nothing_usable(void)
{
    struct fake_buffer bad;

    /* No buffer at all: nothing changes, no signal. */
    reset_state();
    on_process(NULL);
    CHECK(g.held == NULL && g.sequence == 0 && signals == 0 && queued_len == 0);

    /* Only an unusable buffer: it goes back at once, nothing is held or counted. */
    init_buffer(&bad);
    bad.chunk.size = 0;
    struct pw_buffer *s[] = { &bad.pw };
    set_script(1, s);
    on_process(NULL);
    CHECK(queued_len == 1 && queued[0] == &bad.pw);
    CHECK(g.held == NULL && g.held_data == NULL && g.sequence == 0 && signals == 0);
    clear_state();
}

static void test_process_keeps_the_newest(void)
{
    struct fake_buffer a, bad, b, c;
    reset_state();
    init_buffer(&a);
    init_buffer(&bad);
    bad.chunk.flags = SPA_CHUNK_FLAG_CORRUPTED;
    init_buffer(&b);
    b.chunk.offset = 8;
    b.chunk.stride = 20;
    b.data.maxsize = 64;

    /* [A, bad, B]: bad goes back when seen, A when B replaces it; B is held from its chunk offset with its stride. */
    struct pw_buffer *s[] = { &a.pw, &bad.pw, &b.pw };
    set_script(3, s);
    int64_t before = monotonic_ns();
    on_process(NULL);
    int64_t after = monotonic_ns();
    CHECK(queued_len == 2 && queued[0] == &bad.pw && queued[1] == &a.pw);
    CHECK(g.held == &b.pw);
    CHECK(g.held_data == b.mem + 8);
    CHECK(g.held_stride == 20);
    CHECK(g.sequence == 1);
    CHECK(signals == 1);
    /* No header meta: the receive time. */
    CHECK(g.time.from_meta == 0 && g.time.pts_ns >= before && g.time.pts_ns <= after);

    /* The next frame releases the held one (queued back), and a stride of 0 is the row. */
    init_buffer(&c);
    c.chunk.stride = 0;
    struct pw_buffer *s2[] = { &c.pw };
    set_script(1, s2);
    queued_len = 0;
    on_process(NULL);
    CHECK(queued_len == 1 && queued[0] == &b.pw);
    CHECK(g.held == &c.pw && g.held_data == c.mem && g.held_stride == 16);
    CHECK(g.sequence == 2 && signals == 2);

    /* A round with nothing new keeps the held frame. */
    queued_len = 0;
    on_process(NULL);
    CHECK(g.held == &c.pw && g.sequence == 2 && signals == 2 && queued_len == 0);
    clear_state();
}

static void test_process_frame_time(void)
{
    struct fake_buffer b;
    struct pw_buffer *s[] = { &b.pw };

    /* A recent header PTS is the frame time. */
    reset_state();
    init_buffer(&b);
    int64_t pts = monotonic_ns() - 1000000;
    add_header(&b, pts, 0);
    set_script(1, s);
    on_process(NULL);
    CHECK(g.time.from_meta == 1 && g.time.pts_ns == pts);

    /* A corrupted header is not trusted: the receive time. */
    reset_state();
    init_buffer(&b);
    pts = monotonic_ns() - 1000000;
    add_header(&b, pts, SPA_META_HEADER_FLAG_CORRUPTED);
    set_script(1, s);
    on_process(NULL);
    CHECK(g.time.from_meta == 0 && g.time.pts_ns > pts);

    /* A header meta smaller than spa_meta_header is not read. */
    reset_state();
    init_buffer(&b);
    pts = monotonic_ns() - 1000000;
    add_header(&b, pts, 0);
    b.meta.size = sizeof(b.header) - 1;
    set_script(1, s);
    on_process(NULL);
    CHECK(g.time.from_meta == 0 && g.time.pts_ns > pts);
    clear_state();
}

/* ---- on_remove_buffer -------------------------------------------------------------------------------------------- */

static void test_remove_buffer(void)
{
    struct fake_buffer a, b;
    reset_state();
    init_buffer(&a);
    init_buffer(&b);
    g.held = &a.pw;
    g.held_data = a.mem;
    g.held_stride = 16;

    /* Another buffer going away does not touch the held one. */
    on_remove_buffer(NULL, &b.pw);
    CHECK(g.held == &a.pw && g.held_data == a.mem);
    /* The held one going away is forgotten, not queued back (the stream frees it). */
    on_remove_buffer(NULL, &a.pw);
    CHECK(g.held == NULL && g.held_data == NULL);
    CHECK(queued_len == 0);
    clear_state();
}

/* ---- on_state_changed / release_held ----------------------------------------------------------------------------- */

static void test_state_changed(void)
{
    /* Only an error and the end of the connection are errors (a waiting grab is woken); the other states are not. */
    static const enum pw_stream_state calm[] = { PW_STREAM_STATE_CONNECTING, PW_STREAM_STATE_PAUSED, PW_STREAM_STATE_STREAMING };
    for (size_t i = 0; i < sizeof(calm) / sizeof(calm[0]); i++) {
        reset_state();
        on_state_changed(NULL, PW_STREAM_STATE_UNCONNECTED, calm[i], NULL);
        CHECK(g.error == 0 && signals == 0);
    }
    reset_state();
    on_state_changed(NULL, PW_STREAM_STATE_STREAMING, PW_STREAM_STATE_ERROR, "test");
    CHECK(g.error == HIDERCAP_STREAM_ERROR && signals == 1);
    reset_state();
    on_state_changed(NULL, PW_STREAM_STATE_STREAMING, PW_STREAM_STATE_UNCONNECTED, NULL);
    CHECK(g.error == HIDERCAP_STREAM_ERROR && signals == 1);
    clear_state();
}

static void test_release_held(void)
{
    struct fake_buffer a;
    init_buffer(&a);

    /* Held on a stream: queued back and forgotten. */
    reset_state();
    g.held = &a.pw;
    g.held_data = a.mem;
    release_held();
    CHECK(queued_len == 1 && queued[0] == &a.pw && g.held == NULL && g.held_data == NULL);

    /* Nothing held: nothing queued. */
    release_held();
    CHECK(queued_len == 1);

    /* No stream (it is gone): only forgotten. */
    reset_state();
    g.stream = NULL;
    g.held = &a.pw;
    g.held_data = a.mem;
    release_held();
    CHECK(queued_len == 0 && g.held == NULL && g.held_data == NULL);
    clear_state();
}

/* ---- on_param_changed -------------------------------------------------------------------------------------------- */

static const struct spa_pod *video_format(struct spa_pod_builder *_b, uint32_t _id, uint32_t _format, uint32_t _w, uint32_t _h)
{
    struct spa_video_info_raw info;
    spa_zero(info);
    info.format = _format;
    info.size = SPA_RECTANGLE(_w, _h);
    info.framerate = SPA_FRACTION(30, 1);
    return spa_format_video_raw_build(_b, _id, &info);
}

/* A state to compare against: a 7x7 RGBA format and a held buffer, as before a format change. */
static void sentinel_state(struct fake_buffer *_held)
{
    reset_state();
    init_buffer(_held);
    g.format.format = SPA_VIDEO_FORMAT_RGBA;
    g.format.size = SPA_RECTANGLE(7, 7);
    g.held = &_held->pw;
    g.held_data = _held->mem;
}

static int sentinel_unchanged(const struct fake_buffer *_held)
{
    return g.format.format == SPA_VIDEO_FORMAT_RGBA && g.format.size.width == 7 && g.format.size.height == 7
        && g.has_format == 1 && g.held == &_held->pw && g.held_data == _held->mem && queued_len == 0 && update.calls == 0;
}

static void test_param_changed_ignored(void)
{
    struct fake_buffer held;
    uint8_t mem[1024];
    struct spa_pod_builder b;

    /* A format cleared (NULL), and another parameter than the format. */
    sentinel_state(&held);
    on_param_changed(NULL, SPA_PARAM_Format, NULL);
    CHECK(sentinel_unchanged(&held));
    b = (struct spa_pod_builder)SPA_POD_BUILDER_INIT(mem, sizeof(mem));
    on_param_changed(NULL, SPA_PARAM_Props, video_format(&b, SPA_PARAM_Format, SPA_VIDEO_FORMAT_BGRx, 640, 480));
    CHECK(sentinel_unchanged(&held));

    /* Not a format object at all (spa_format_parse fails): no media type. */
    b = (struct spa_pod_builder)SPA_POD_BUILDER_INIT(mem, sizeof(mem));
    const struct spa_pod *no_type = spa_pod_builder_add_object(&b, SPA_TYPE_OBJECT_Format, SPA_PARAM_Format,
        SPA_FORMAT_mediaSubtype, SPA_POD_Id(SPA_MEDIA_SUBTYPE_raw));
    on_param_changed(NULL, SPA_PARAM_Format, no_type);
    CHECK(sentinel_unchanged(&held));

    /* Audio, and video that is not raw. */
    b = (struct spa_pod_builder)SPA_POD_BUILDER_INIT(mem, sizeof(mem));
    struct spa_audio_info_raw audio;
    spa_zero(audio);
    audio.format = SPA_AUDIO_FORMAT_S16;
    audio.rate = 48000;
    audio.channels = 2;
    on_param_changed(NULL, SPA_PARAM_Format, spa_format_audio_raw_build(&b, SPA_PARAM_Format, &audio));
    CHECK(sentinel_unchanged(&held));
    b = (struct spa_pod_builder)SPA_POD_BUILDER_INIT(mem, sizeof(mem));
    const struct spa_pod *mjpg = spa_pod_builder_add_object(&b, SPA_TYPE_OBJECT_Format, SPA_PARAM_Format,
        SPA_FORMAT_mediaType, SPA_POD_Id(SPA_MEDIA_TYPE_video),
        SPA_FORMAT_mediaSubtype, SPA_POD_Id(SPA_MEDIA_SUBTYPE_mjpg),
        SPA_FORMAT_VIDEO_size, SPA_POD_Rectangle(&SPA_RECTANGLE(640, 480)));
    on_param_changed(NULL, SPA_PARAM_Format, mjpg);
    CHECK(sentinel_unchanged(&held));

    /* Raw video without a size (or a zero one): the previous format stays whole. */
    b = (struct spa_pod_builder)SPA_POD_BUILDER_INIT(mem, sizeof(mem));
    on_param_changed(NULL, SPA_PARAM_Format, video_format(&b, SPA_PARAM_Format, SPA_VIDEO_FORMAT_BGRx, 0, 0));
    CHECK(sentinel_unchanged(&held));
    b = (struct spa_pod_builder)SPA_POD_BUILDER_INIT(mem, sizeof(mem));
    const struct spa_pod *zero_width = spa_pod_builder_add_object(&b, SPA_TYPE_OBJECT_Format, SPA_PARAM_Format,
        SPA_FORMAT_mediaType, SPA_POD_Id(SPA_MEDIA_TYPE_video),
        SPA_FORMAT_mediaSubtype, SPA_POD_Id(SPA_MEDIA_SUBTYPE_raw),
        SPA_FORMAT_VIDEO_format, SPA_POD_Id(SPA_VIDEO_FORMAT_BGRx),
        SPA_FORMAT_VIDEO_size, SPA_POD_Rectangle(&SPA_RECTANGLE(0, 480)));
    on_param_changed(NULL, SPA_PARAM_Format, zero_width);
    CHECK(sentinel_unchanged(&held));
    b = (struct spa_pod_builder)SPA_POD_BUILDER_INIT(mem, sizeof(mem));
    const struct spa_pod *zero_height = spa_pod_builder_add_object(&b, SPA_TYPE_OBJECT_Format, SPA_PARAM_Format,
        SPA_FORMAT_mediaType, SPA_POD_Id(SPA_MEDIA_TYPE_video),
        SPA_FORMAT_mediaSubtype, SPA_POD_Id(SPA_MEDIA_SUBTYPE_raw),
        SPA_FORMAT_VIDEO_format, SPA_POD_Id(SPA_VIDEO_FORMAT_BGRx),
        SPA_FORMAT_VIDEO_size, SPA_POD_Rectangle(&SPA_RECTANGLE(640, 0)));
    on_param_changed(NULL, SPA_PARAM_Format, zero_height);
    CHECK(sentinel_unchanged(&held));
    clear_state();
}

static void test_param_changed_takes_the_format(void)
{
    struct fake_buffer held;
    uint8_t mem[1024];
    struct spa_pod_builder b = SPA_POD_BUILDER_INIT(mem, sizeof(mem));

    /* A first format (nothing held): taken, and the buffers and header meta are asked for. */
    reset_state();
    g.has_format = 0;
    spa_zero(g.format);
    on_param_changed(NULL, SPA_PARAM_Format, video_format(&b, SPA_PARAM_Format, SPA_VIDEO_FORMAT_BGRA, 640, 480));
    CHECK(g.has_format == 1);
    CHECK(g.format.format == SPA_VIDEO_FORMAT_BGRA && g.format.size.width == 640 && g.format.size.height == 480);
    CHECK(g.format.framerate.num == 30 && g.format.framerate.denom == 1);
    CHECK(queued_len == 0);
    CHECK(update.calls == 1 && update.n_params == 2);
    CHECK(update.types[0] == SPA_TYPE_OBJECT_ParamBuffers && update.ids[0] == SPA_PARAM_Buffers);
    CHECK(update.buffers_default == 4 && update.buffers_min == 3 && update.buffers_max == 16);
    CHECK(update.data_types == ((1 << SPA_DATA_MemFd) | (1 << SPA_DATA_MemPtr)));
    CHECK(update.types[1] == SPA_TYPE_OBJECT_ParamMeta && update.ids[1] == SPA_PARAM_Meta);
    CHECK(update.meta_type == SPA_META_Header && update.meta_size == (int32_t)sizeof(struct spa_meta_header));

    /* A format change while a frame is held: the frame (laid out for the old format) goes back, the new one is taken. */
    sentinel_state(&held);
    b = (struct spa_pod_builder)SPA_POD_BUILDER_INIT(mem, sizeof(mem));
    on_param_changed(NULL, SPA_PARAM_Format, video_format(&b, SPA_PARAM_Format, SPA_VIDEO_FORMAT_RGBx, 1920, 1080));
    CHECK(queued_len == 1 && queued[0] == &held.pw);
    CHECK(g.held == NULL && g.held_data == NULL);
    CHECK(g.format.format == SPA_VIDEO_FORMAT_RGBx && g.format.size.width == 1920 && g.format.size.height == 1080);
    CHECK(update.calls == 1);
    clear_state();
}

/* ---- copy_held --------------------------------------------------------------------------------------------------- */

enum { CW = 3, CH = 4, CSTRIDE = CW * 4 + 12 };

/* The source frame: byte c of pixel (x, y) is 0x10 * y + 4 * x + c (all distinct); the 12 padding bytes per row are 0xEE. */
static void fill_source(uint8_t *_src)
{
    memset(_src, 0xEE, CSTRIDE * CH);
    for (int y = 0; y < CH; y++) {
        for (int x = 0; x < CW; x++) {
            for (int c = 0; c < 4; c++) {
                _src[y * CSTRIDE + x * 4 + c] = (uint8_t)(0x10 * y + 4 * x + c);
            }
        }
    }
}

/* Checks every byte of a frame copied from the source: rows [row0, row1) are opaque BGRA (R and B swapped for RGB*),
 * the others still 0x55. */
static void check_rows(const uint8_t *_dst, const uint8_t *_src, int _swap, int32_t _row0, int32_t _row1)
{
    for (int y = 0; y < CH; y++) {
        for (int x = 0; x < CW; x++) {
            const uint8_t *s = _src + y * CSTRIDE + x * 4;
            const uint8_t *d = _dst + (y * CW + x) * 4;
            if (y >= _row0 && y < _row1) {
                CHECK(d[0] == (_swap ? s[2] : s[0]));
                CHECK(d[1] == s[1]);
                CHECK(d[2] == (_swap ? s[0] : s[2]));
                CHECK(d[3] == 0xFF);
            } else {
                CHECK(d[0] == 0x55 && d[1] == 0x55 && d[2] == 0x55 && d[3] == 0x55);
            }
        }
    }
}

/* Copies rows [row0, row1) of the source in _format into a 0x55-filled frame and checks every byte. */
static void check_copy(uint32_t _format, int _swap, int32_t _row0, int32_t _row1)
{
    uint8_t src[CSTRIDE * CH];
    uint8_t dst[CW * CH * 4];
    fill_source(src);
    memset(dst, 0x55, sizeof(dst));
    reset_state();
    g.format.format = _format;
    g.format.size = SPA_RECTANGLE(CW, CH);
    g.held_data = src;
    g.held_stride = CSTRIDE;
    copy_held(dst, _row0, _row1);
    check_rows(dst, src, _swap, _row0, _row1);
    clear_state();
}

static void test_copy_held(void)
{
    static const struct { uint32_t format; int swap; } formats[] = {
        { SPA_VIDEO_FORMAT_BGRx, 0 },
        { SPA_VIDEO_FORMAT_BGRA, 0 },
        { SPA_VIDEO_FORMAT_RGBx, 1 },
        { SPA_VIDEO_FORMAT_RGBA, 1 },
    };
    for (size_t i = 0; i < sizeof(formats) / sizeof(formats[0]); i++) {
        check_copy(formats[i].format, formats[i].swap, 0, CH); /* whole frame */
        check_copy(formats[i].format, formats[i].swap, 1, 3);  /* middle rows only */
        check_copy(formats[i].format, formats[i].swap, 2, 2);  /* empty range: nothing written */
        check_copy(formats[i].format, formats[i].swap, 3, 4);  /* last row */
    }
}

/* ---- hidercap_grab_after with a held frame ----------------------------------------------------------------------- */

/* A grab on a started stream (whose remote never answers) after a frame was put in place as on_process would: the rows
 * asked for, clamped to the frame, the frame description, the size check, and a frame that is too old. */
static void test_grab_after_with_a_held_frame(void)
{
    int sv[2];
    CHECK(socketpair(AF_UNIX, SOCK_STREAM | SOCK_CLOEXEC, 0, sv) == 0);
    CHECK(hidercap_start(sv[0], 42) == HIDERCAP_OK);
    uint8_t src[CSTRIDE * CH];
    fill_source(src);
    struct fake_buffer held;
    init_buffer(&held);
    int64_t pts = monotonic_ns();
    pw_thread_loop_lock(g.loop);
    g.format.format = SPA_VIDEO_FORMAT_RGBA;
    g.format.size = SPA_RECTANGLE(CW, CH);
    g.has_format = 1;
    g.held = &held.pw;
    g.held_data = src;
    g.held_stride = CSTRIDE;
    g.time.pts_ns = pts;
    g.time.from_meta = 1;
    g.sequence = 7;
    pw_thread_loop_unlock(g.loop);

    static const struct { int32_t row0, row1, from, to; } cases[] = {
        { 1, 3, 1, 3 },       /* as asked */
        { 0, CH, 0, CH },     /* the whole frame */
        { -5, 2, 0, 2 },      /* row0 below 0 */
        { 2, 100, 2, CH },    /* row1 past the frame */
        { 3, 1, 3, 3 },       /* row1 before row0: nothing */
        { 100, 200, CH, CH }, /* both past the frame: nothing */
    };
    uint8_t dst[CW * CH * 4];
    for (size_t i = 0; i < sizeof(cases) / sizeof(cases[0]); i++) {
        memset(dst, 0x55, sizeof(dst));
        hidercap_frame f;
        memset(&f, 0xAB, sizeof(f));
        CHECK(hidercap_grab_after(pts, 0, cases[i].row0, cases[i].row1, dst, sizeof(dst), &f) == HIDERCAP_OK);
        CHECK(f.width == CW && f.height == CH && f.stride == CW * 4);
        CHECK(f.pts_ns == pts && f.pts_from_meta == 1 && f.sequence == 7);
        check_rows(dst, src, 1, cases[i].from, cases[i].to);
    }

    /* The buffer must hold the whole frame (rows land at their own place): the size needed is reported, nothing copied. */
    static const int32_t sizes[] = { -1, 0, CW * CH * 4 - 1 };
    for (size_t i = 0; i < sizeof(sizes) / sizeof(sizes[0]); i++) {
        memset(dst, 0x55, sizeof(dst));
        hidercap_frame f;
        CHECK(hidercap_grab_after(pts, 0, 0, CH, dst, sizes[i], &f) == HIDERCAP_BUFFER_TOO_SMALL);
        CHECK(f.width == CW && f.height == CH && f.stride == CW * 4 && f.sequence == 7);
        check_rows(dst, src, 1, 0, 0);
    }
    hidercap_frame f;
    CHECK(hidercap_grab_after(pts, 0, 0, CH, NULL, (int32_t)sizeof(dst), &f) == HIDERCAP_BUFFER_TOO_SMALL);
    CHECK(f.width == CW && f.height == CH);

    /* A frame shown before not_before: the grab waits for a newer one and times out, with the sequence only. */
    memset(&f, 0xAB, sizeof(f));
    CHECK(hidercap_grab_after(pts + 1, 50, 0, CH, dst, sizeof(dst), &f) == HIDERCAP_TIMEOUT);
    CHECK(f.sequence == 7 && f.width == 0 && f.height == 0 && f.pts_ns == 0);
    CHECK(hidercap_last_sequence() == 7);

    /* A stream error wins over the held frame. */
    pw_thread_loop_lock(g.loop);
    g.error = HIDERCAP_STREAM_ERROR;
    pw_thread_loop_unlock(g.loop);
    memset(&f, 0xAB, sizeof(f));
    CHECK(hidercap_grab_after(pts, 0, 0, CH, dst, sizeof(dst), &f) == HIDERCAP_STREAM_ERROR);
    CHECK(f.sequence == 7 && f.width == 0);

    /* The fake buffer must not reach the real stream on stop. */
    pw_thread_loop_lock(g.loop);
    g.held = NULL;
    g.held_data = NULL;
    pw_thread_loop_unlock(g.loop);
    hidercap_stop();
    close(sv[1]);
}

/* ---- hidercap_start failures ------------------------------------------------------------------------------------- */

static int is_closed(int _fd)
{
    return fcntl(_fd, F_GETFD) == -1 && errno == EBADF;
}

/* Reads the other end of a socket pair until EOF (what the client wrote first is skipped): 1 on EOF within 5 s. */
static int peer_sees_eof(int _peer)
{
    uint8_t buf[4096];
    for (;;) {
        struct pollfd p = { .fd = _peer, .events = POLLIN };
        if (poll(&p, 1, 5000) != 1) {
            return 0;
        }
        ssize_t n = read(_peer, buf, sizeof(buf));
        if (n == 0) {
            return 1;
        }
        if (n < 0) {
            return 0;
        }
    }
}

static void check_start_fails_at(enum start_step _step, int _fd_closed_by_start)
{
    int sv[2];
    CHECK(socketpair(AF_UNIX, SOCK_STREAM | SOCK_CLOEXEC, 0, sv) == 0);
    fail_at = _step;
    CHECK(hidercap_start(sv[0], 42) == HIDERCAP_STREAM_ERROR);
    fail_at = FAIL_NONE;
    /* Everything is released: the fd (closed by start itself before it was handed to PipeWire, by the disconnect
     * after), the loop and the state. */
    if (_fd_closed_by_start) {
        CHECK(is_closed(sv[0]));
    }
    CHECK(peer_sees_eof(sv[1]));
    close(sv[1]);
    CHECK(g.loop == NULL && g.context == NULL && g.core == NULL && g.stream == NULL);
    CHECK(pw_initialized == 0);
    hidercap_frame f;
    memset(&f, 0xAB, sizeof(f));
    CHECK(hidercap_grab_after(0, 0, 0, 1, NULL, 0, &f) == HIDERCAP_NOT_STARTED);
    CHECK(hidercap_last_sequence() == 0);

    /* A start after the failure works. */
    CHECK(socketpair(AF_UNIX, SOCK_STREAM | SOCK_CLOEXEC, 0, sv) == 0);
    CHECK(hidercap_start(sv[0], 42) == HIDERCAP_OK);
    CHECK(g.loop != NULL && g.stream != NULL);
    hidercap_stop();
    close(sv[1]);
}

static void test_start_failures(void)
{
    check_start_fails_at(FAIL_LOOP_NEW, 1);
    check_start_fails_at(FAIL_CONTEXT_NEW, 1);
    check_start_fails_at(FAIL_LOOP_START, 1);
    check_start_fails_at(FAIL_STREAM_NEW, 0);
    check_start_fails_at(FAIL_CONNECT, 0);
}

int main(void)
{
    test_usable();
    test_process_nothing_usable();
    test_process_keeps_the_newest();
    test_process_frame_time();
    test_remove_buffer();
    test_state_changed();
    test_release_held();
    test_param_changed_ignored();
    test_param_changed_takes_the_format();
    test_copy_held();
    test_grab_after_with_a_held_frame();
    test_start_failures();
    if (failures == 0) {
        printf("test_hidercap_internal: all checks passed\n");
    }
    return failures == 0 ? 0 : 1;
}
