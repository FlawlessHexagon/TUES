using System;

void Test(int globalX) {
    int chunkX = globalX >> 4;
    int localX = globalX & 15;
    int restoredGlobal = chunkX * 16 + localX;
    Console.WriteLine($"global: {globalX} -> chunk: {chunkX}, local: {localX} -> restored: {restoredGlobal} (Matches? {globalX == restoredGlobal})");
}

Test(0);
Test(-1);
Test(-16);
Test(-17);
Test(15);
Test(16);
