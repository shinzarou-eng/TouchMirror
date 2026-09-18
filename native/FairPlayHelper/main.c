#include <stdio.h>
#include <stdlib.h>
#include <fcntl.h>
#include <io.h>
#include "playfair.h"

int main(void)
{
    unsigned char input[236];
    unsigned char key[16];
    size_t got;

    _setmode(_fileno(stdin), _O_BINARY);
    _setmode(_fileno(stdout), _O_BINARY);

    got = fread(input, 1, sizeof(input), stdin);
    if (got != sizeof(input)) {
        return 2;
    }

    playfair_decrypt(input, input + 164, key);
    fwrite(key, 1, sizeof(key), stdout);
    fflush(stdout);
    return 0;
}
