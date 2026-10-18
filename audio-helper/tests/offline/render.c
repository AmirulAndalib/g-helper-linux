/* Offline renderer: runs the real ghelper-audio DSP chain on a file.
 *
 *   render [CMD ...] < in.f32 > out.f32
 *
 * Input and output are raw mono float32 at 48 kHz. Each argument is one
 * stdin protocol line ("VOC 1", "VOP 900 110 2 12 50 1 0", ...) and goes
 * through the same parse_cmd() as the live helper, before any audio.
 * Blocks of 1024 samples match a common PipeWire quantum. */

#define GHA_NO_MAIN
/* The PipeWire callbacks main() wires up are unused here. */
#pragma GCC diagnostic ignored "-Wunused-function"
#include "../../main.c"

#define BLOCK 1024

int main(int argc, char *argv[])
{
    if (dsp_init() < 0)
        return 1;

    char line[512];
    for (int i = 1; i < argc; i++)
    {
        snprintf(line, sizeof(line), "%s", argv[i]);
        parse_cmd(line);
    }

    float in[BLOCK];
    float out[POST_RING_CAP];
    size_t n;
    while ((n = fread(in, sizeof(float), BLOCK, stdin)) > 0)
    {
        dsp_process_block(in, (uint32_t)n);

        uint32_t head = atomic_load(&g_post.head);
        uint32_t tail = atomic_load(&g_post.tail);
        size_t k = 0;
        while (tail != head)
        {
            out[k++] = g_post.buf[tail];
            tail = (tail + 1) % POST_RING_CAP;
        }
        atomic_store(&g_post.tail, tail);
        fwrite(out, sizeof(float), k, stdout);
    }
    rnnoise_destroy(g_rt.rn);
    return 0;
}
