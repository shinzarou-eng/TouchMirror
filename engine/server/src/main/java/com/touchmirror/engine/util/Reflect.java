package com.touchmirror.engine.util;

import java.lang.reflect.Method;

public final class Reflect {
    private Reflect() {
    }

    public static Method lookup(Class<?> cls, String name, Class<?>... params) {
        try {
            return cls.getMethod(name, params);
        } catch (NoSuchMethodException e) {
            return null;
        }
    }

    public static Method lookupOrThrow(Class<?> cls, String name, Class<?>... params) throws NoSuchMethodException {
        Method method = lookup(cls, name, params);
        if (method == null) {
            throw new NoSuchMethodException(name);
        }
        return method;
    }

    public static Object invoke(Method method, Object target, Object... args) {
        try {
            return method.invoke(target, args);
        } catch (ReflectiveOperationException e) {
            Ln.e("Could not invoke method", e);
            return null;
        }
    }
}
