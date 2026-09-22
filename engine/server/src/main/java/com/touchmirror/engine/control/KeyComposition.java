package com.touchmirror.engine.control;

import java.util.HashMap;
import java.util.Map;

public final class KeyComposition {

    private static final String GRAVE_PAIRS = "ÀAÈEÌIÒOÙUàaèeìiòoùuǸNǹnẀWẁwỲYỳy";
    private static final String ACUTE_PAIRS = "ÁAÉEÍIÓOÚUÝYáaéeíióoúuýyĆCćcĹLĺlŃNńnŔRŕrŚSśsŹZźzǴGǵgḈÇḉçḰKḱkḾMḿmṔPṕpẂWẃw";
    private static final String CIRCUMFLEX_PAIRS = "ÂAÊEÎIÔOÛUâaêeîiôoûuĈCĉcĜGĝgĤHĥhĴJĵjŜSŝsŴWŵwŶYŷyẐZẑz";
    private static final String TILDE_PAIRS = "ÃAÑNÕOãañnõoĨIĩiŨUũuẼEẽeỸYỹy";
    private static final String UMLAUT_PAIRS = "ÄAËEÏIÖOÜUäaëeïiöoüuÿyŸYḦHḧhẄWẅwẌXẍxẗt";

    private static final Map<Character, String> DECOMPOSITIONS = buildMap();

    private KeyComposition() {
    }

    public static String decompose(char c) {
        return DECOMPOSITIONS.get(c);
    }

    private static Map<Character, String> buildMap() {
        Map<Character, String> map = new HashMap<>();
        register(map, GRAVE_PAIRS, '̀');
        register(map, ACUTE_PAIRS, '́');
        register(map, CIRCUMFLEX_PAIRS, '̂');
        register(map, TILDE_PAIRS, '̃');
        register(map, UMLAUT_PAIRS, '̈');
        return map;
    }

    private static void register(Map<Character, String> map, String pairs, char deadKey) {
        for (int i = 0; i + 1 < pairs.length(); i += 2) {
            map.put(pairs.charAt(i), deadKey + String.valueOf(pairs.charAt(i + 1)));
        }
    }
}
