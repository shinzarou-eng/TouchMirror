package com.touchmirror.engine.util;

import com.touchmirror.engine.AndroidVersions;

import android.annotation.TargetApi;

import java.util.function.Predicate;

public final class BinarySearch {
    private BinarySearch() {
    }

    @TargetApi(AndroidVersions.API_24_ANDROID_7_0)
    public static int findHighestTrue(int low, int high, Predicate<Integer> predicate) {
        if (low <= high) {
            if (predicate.test(high)) {
                return high;
            }
            --high;
        }

        int result = low - 1;
        while (low <= high) {
            int mid = low + (high - low + 1) / 2;
            if (predicate.test(mid)) {
                result = mid;

                low = mid + 1;
            } else {
                high = mid - 1;
            }
        }

        return result;
    }
}
