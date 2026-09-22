package com.touchmirror.engine;

import com.touchmirror.engine.audio.AudioCaptureException;
import com.touchmirror.engine.util.Ln;

import android.annotation.SuppressLint;
import android.annotation.TargetApi;
import android.app.Application;
import android.app.Instrumentation;
import android.content.AttributionSource;
import android.content.Context;
import android.content.pm.ApplicationInfo;
import android.media.AudioAttributes;
import android.media.AudioManager;
import android.media.AudioRecord;
import android.os.Build;
import android.os.Looper;
import android.os.Parcel;

import java.lang.ref.WeakReference;
import java.lang.reflect.Constructor;
import java.lang.reflect.Field;
import java.lang.reflect.Method;

@SuppressLint("PrivateApi,BlockedPrivateApi,SoonBlockedPrivateApi,DiscouragedPrivateApi")
public final class Workarounds {

    private static final Class<?> ACTIVITY_THREAD_CLASS;
    private static final Object ACTIVITY_THREAD;

    static {
        try {
            ACTIVITY_THREAD_CLASS = Class.forName("android.app.ActivityThread");

            Constructor<?> ctor = ACTIVITY_THREAD_CLASS.getDeclaredConstructor();
            ctor.setAccessible(true);
            ACTIVITY_THREAD = ctor.newInstance();

            setField(ACTIVITY_THREAD_CLASS, "sCurrentActivityThread", null, ACTIVITY_THREAD);
            setField(ACTIVITY_THREAD_CLASS, "mSystemThread", ACTIVITY_THREAD, true);
        } catch (Exception e) {
            throw new AssertionError(e);
        }
    }

    private Workarounds() {
    }

    private static void setField(Class<?> cls, String name, Object target, Object value) throws ReflectiveOperationException {
        Field field = cls.getDeclaredField(name);
        field.setAccessible(true);
        field.set(target, value);
    }

    public static void apply() {
        if (Build.VERSION.SDK_INT >= AndroidVersions.API_31_ANDROID_12) {
            fillConfigurationController();
        }

        if (!Build.BRAND.equalsIgnoreCase("ONYX")) {
            fillAppInfo();
        }

        fillAppContext();
    }

    private static void fillAppInfo() {
        try {
            Class<?> appBindDataClass = Class.forName("android.app.ActivityThread$AppBindData");
            Constructor<?> ctor = appBindDataClass.getDeclaredConstructor();
            ctor.setAccessible(true);
            Object appBindData = ctor.newInstance();

            ApplicationInfo applicationInfo = new ApplicationInfo();
            applicationInfo.packageName = FakeContext.PACKAGE_NAME;

            setField(appBindDataClass, "appInfo", appBindData, applicationInfo);
            setField(ACTIVITY_THREAD_CLASS, "mBoundApplication", ACTIVITY_THREAD, appBindData);
        } catch (Throwable t) {
            Ln.d("Could not fill app info: " + t.getMessage());
        }
    }

    private static void fillAppContext() {
        try {
            Application app = Instrumentation.newApplication(Application.class, FakeContext.get());
            setField(ACTIVITY_THREAD_CLASS, "mInitialApplication", ACTIVITY_THREAD, app);
        } catch (Throwable t) {
            Ln.d("Could not fill app context: " + t.getMessage());
        }
    }

    private static void fillConfigurationController() {
        try {
            Class<?> configurationControllerClass = Class.forName("android.app.ConfigurationController");
            Class<?> activityThreadInternalClass = Class.forName("android.app.ActivityThreadInternal");

            Constructor<?> ctor = configurationControllerClass.getDeclaredConstructor(activityThreadInternalClass);
            ctor.setAccessible(true);
            Object configurationController = ctor.newInstance(ACTIVITY_THREAD);

            setField(ACTIVITY_THREAD_CLASS, "mConfigurationController", ACTIVITY_THREAD, configurationController);
        } catch (Throwable t) {
            Ln.d("Could not fill configuration: " + t.getMessage());
        }
    }

    static Context getSystemContext() {
        try {
            Method getSystemContext = ACTIVITY_THREAD_CLASS.getDeclaredMethod("getSystemContext");
            return (Context) getSystemContext.invoke(ACTIVITY_THREAD);
        } catch (Throwable t) {
            Ln.d("Could not get system context: " + t.getMessage());
            return null;
        }
    }

    @TargetApi(AndroidVersions.API_30_ANDROID_11)
    @SuppressLint("WrongConstant,MissingPermission")
    public static AudioRecord createAudioRecord(int source, int sampleRate, int channelConfig, int channels, int channelMask,
            int encoding) throws AudioCaptureException {
        try {
            Constructor<AudioRecord> ctor = AudioRecord.class.getDeclaredConstructor(long.class);
            ctor.setAccessible(true);
            AudioRecord audioRecord = ctor.newInstance(0L);

            setField(AudioRecord.class, "mRecordingState", audioRecord, AudioRecord.RECORDSTATE_STOPPED);

            Looper looper = Looper.myLooper() != null ? Looper.myLooper() : Looper.getMainLooper();
            setField(AudioRecord.class, "mInitializationLooper", audioRecord, looper);

            AudioAttributes.Builder attributesBuilder = new AudioAttributes.Builder();
            Method setInternalCapturePreset = AudioAttributes.Builder.class.getMethod("setInternalCapturePreset", int.class);
            setInternalCapturePreset.invoke(attributesBuilder, source);
            AudioAttributes attributes = attributesBuilder.build();
            setField(AudioRecord.class, "mAudioAttributes", audioRecord, attributes);

            Method audioParamCheck = AudioRecord.class.getDeclaredMethod("audioParamCheck", int.class, int.class, int.class);
            audioParamCheck.setAccessible(true);
            audioParamCheck.invoke(audioRecord, source, sampleRate, encoding);

            setField(AudioRecord.class, "mChannelCount", audioRecord, channels);
            setField(AudioRecord.class, "mChannelMask", audioRecord, channelMask);

            int bufferSize = AudioRecord.getMinBufferSize(sampleRate, channelConfig, encoding) * 8;
            Method audioBuffSizeCheck = AudioRecord.class.getDeclaredMethod("audioBuffSizeCheck", int.class);
            audioBuffSizeCheck.setAccessible(true);
            audioBuffSizeCheck.invoke(audioRecord, bufferSize);

            int[] sampleRateHolder = {sampleRate};
            int[] sessionHolder = {AudioManager.AUDIO_SESSION_ID_GENERATE};

            int initResult = nativeSetup(audioRecord, attributes, sampleRateHolder, channelMask, bufferSize, sessionHolder);
            if (initResult != AudioRecord.SUCCESS) {
                Ln.e("Error code " + initResult + " when initializing native AudioRecord object.");
                throw new RuntimeException("Cannot create AudioRecord");
            }

            setField(AudioRecord.class, "mSampleRate", audioRecord, sampleRateHolder[0]);
            setField(AudioRecord.class, "mSessionId", audioRecord, sessionHolder[0]);
            setField(AudioRecord.class, "mState", audioRecord, AudioRecord.STATE_INITIALIZED);

            return audioRecord;
        } catch (Exception e) {
            Ln.e("Cannot create AudioRecord", e);
            throw new AudioCaptureException();
        }
    }

    private static int nativeSetup(AudioRecord audioRecord, AudioAttributes attributes, int[] sampleRateHolder, int channelMask,
            int bufferSize, int[] sessionHolder) throws Exception {
        if (Build.VERSION.SDK_INT < AndroidVersions.API_31_ANDROID_12) {
            Method nativeSetup = AudioRecord.class.getDeclaredMethod("native_setup", Object.class, Object.class, int[].class, int.class,
                    int.class, int.class, int.class, int[].class, String.class, long.class);
            nativeSetup.setAccessible(true);
            return (int) nativeSetup.invoke(audioRecord, new WeakReference<>(audioRecord), attributes, sampleRateHolder, channelMask, 0,
                    audioRecord.getAudioFormat(), bufferSize, sessionHolder, FakeContext.get().getOpPackageName(), 0L);
        }

        AttributionSource attributionSource = FakeContext.get().getAttributionSource();
        Method asScopedParcelState = AttributionSource.class.getDeclaredMethod("asScopedParcelState");
        asScopedParcelState.setAccessible(true);

        try (AutoCloseable state = (AutoCloseable) asScopedParcelState.invoke(attributionSource)) {
            Parcel parcel = (Parcel) state.getClass().getDeclaredMethod("getParcel").invoke(state);

            if (Build.VERSION.SDK_INT < AndroidVersions.API_34_ANDROID_14) {
                Method nativeSetup = AudioRecord.class.getDeclaredMethod("native_setup", Object.class, Object.class, int[].class, int.class,
                        int.class, int.class, int.class, int[].class, Parcel.class, long.class, int.class);
                nativeSetup.setAccessible(true);
                return (int) nativeSetup.invoke(audioRecord, new WeakReference<>(audioRecord), attributes, sampleRateHolder, channelMask, 0,
                        audioRecord.getAudioFormat(), bufferSize, sessionHolder, parcel, 0L, 0);
            }

            Method nativeSetup = AudioRecord.class.getDeclaredMethod("native_setup", Object.class, Object.class, int[].class, int.class,
                    int.class, int.class, int.class, int[].class, Parcel.class, long.class, int.class, int.class);
            nativeSetup.setAccessible(true);
            return (int) nativeSetup.invoke(audioRecord, new WeakReference<>(audioRecord), attributes, sampleRateHolder, channelMask, 0,
                    audioRecord.getAudioFormat(), bufferSize, sessionHolder, parcel, 0L, 0, 0);
        }
    }
}
