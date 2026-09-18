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
            Constructor<?> activityThreadConstructor = ACTIVITY_THREAD_CLASS.getDeclaredConstructor();
            activityThreadConstructor.setAccessible(true);
            ACTIVITY_THREAD = activityThreadConstructor.newInstance();

            Field sCurrentActivityThreadField = ACTIVITY_THREAD_CLASS.getDeclaredField("sCurrentActivityThread");
            sCurrentActivityThreadField.setAccessible(true);
            sCurrentActivityThreadField.set(null, ACTIVITY_THREAD);

            Field mSystemThreadField = ACTIVITY_THREAD_CLASS.getDeclaredField("mSystemThread");
            mSystemThreadField.setAccessible(true);
            mSystemThreadField.setBoolean(ACTIVITY_THREAD, true);
        } catch (Exception e) {
            throw new AssertionError(e);
        }
    }

    private Workarounds() {
    }

    public static void apply() {
        if (Build.VERSION.SDK_INT >= AndroidVersions.API_31_ANDROID_12) {
            fillConfigurationController();
        }

        boolean mustFillAppInfo = !Build.BRAND.equalsIgnoreCase("ONYX");

        if (mustFillAppInfo) {
            fillAppInfo();
        }

        fillAppContext();
    }

    private static void fillAppInfo() {
        try {
            Class<?> appBindDataClass = Class.forName("android.app.ActivityThread$AppBindData");
            Constructor<?> appBindDataConstructor = appBindDataClass.getDeclaredConstructor();
            appBindDataConstructor.setAccessible(true);
            Object appBindData = appBindDataConstructor.newInstance();

            ApplicationInfo applicationInfo = new ApplicationInfo();
            applicationInfo.packageName = FakeContext.PACKAGE_NAME;

            Field appInfoField = appBindDataClass.getDeclaredField("appInfo");
            appInfoField.setAccessible(true);
            appInfoField.set(appBindData, applicationInfo);

            Field mBoundApplicationField = ACTIVITY_THREAD_CLASS.getDeclaredField("mBoundApplication");
            mBoundApplicationField.setAccessible(true);
            mBoundApplicationField.set(ACTIVITY_THREAD, appBindData);
        } catch (Throwable throwable) {
            Ln.d("Could not fill app info: " + throwable.getMessage());
        }
    }

    private static void fillAppContext() {
        try {
            Application app = Instrumentation.newApplication(Application.class, FakeContext.get());

            Field mInitialApplicationField = ACTIVITY_THREAD_CLASS.getDeclaredField("mInitialApplication");
            mInitialApplicationField.setAccessible(true);
            mInitialApplicationField.set(ACTIVITY_THREAD, app);
        } catch (Throwable throwable) {
            Ln.d("Could not fill app context: " + throwable.getMessage());
        }
    }

    private static void fillConfigurationController() {
        try {
            Class<?> configurationControllerClass = Class.forName("android.app.ConfigurationController");
            Class<?> activityThreadInternalClass = Class.forName("android.app.ActivityThreadInternal");

            Constructor<?> configurationControllerConstructor = configurationControllerClass.getDeclaredConstructor(activityThreadInternalClass);
            configurationControllerConstructor.setAccessible(true);
            Object configurationController = configurationControllerConstructor.newInstance(ACTIVITY_THREAD);

            Field configurationControllerField = ACTIVITY_THREAD_CLASS.getDeclaredField("mConfigurationController");
            configurationControllerField.setAccessible(true);
            configurationControllerField.set(ACTIVITY_THREAD, configurationController);
        } catch (Throwable throwable) {
            Ln.d("Could not fill configuration: " + throwable.getMessage());
        }
    }

    static Context getSystemContext() {
        try {
            Method getSystemContextMethod = ACTIVITY_THREAD_CLASS.getDeclaredMethod("getSystemContext");
            return (Context) getSystemContextMethod.invoke(ACTIVITY_THREAD);
        } catch (Throwable throwable) {
            Ln.d("Could not get system context: " + throwable.getMessage());
            return null;
        }
    }

    @TargetApi(AndroidVersions.API_30_ANDROID_11)
    @SuppressLint("WrongConstant,MissingPermission")
    public static AudioRecord createAudioRecord(int source, int sampleRate, int channelConfig, int channels, int channelMask, int encoding) throws
            AudioCaptureException {
        try {
            Constructor<AudioRecord> audioRecordConstructor = AudioRecord.class.getDeclaredConstructor(long.class);
            audioRecordConstructor.setAccessible(true);
            AudioRecord audioRecord = audioRecordConstructor.newInstance(0L);

            Field mRecordingStateField = AudioRecord.class.getDeclaredField("mRecordingState");
            mRecordingStateField.setAccessible(true);
            mRecordingStateField.set(audioRecord, AudioRecord.RECORDSTATE_STOPPED);

            Looper looper = Looper.myLooper();
            if (looper == null) {
                looper = Looper.getMainLooper();
            }

            Field mInitializationLooperField = AudioRecord.class.getDeclaredField("mInitializationLooper");
            mInitializationLooperField.setAccessible(true);
            mInitializationLooperField.set(audioRecord, looper);

            int capturePreset = source;
            AudioAttributes.Builder audioAttributesBuilder = new AudioAttributes.Builder();
            Method setInternalCapturePresetMethod = AudioAttributes.Builder.class.getMethod("setInternalCapturePreset", int.class);
            setInternalCapturePresetMethod.invoke(audioAttributesBuilder, capturePreset);
            AudioAttributes attributes = audioAttributesBuilder.build();

            Field mAudioAttributesField = AudioRecord.class.getDeclaredField("mAudioAttributes");
            mAudioAttributesField.setAccessible(true);
            mAudioAttributesField.set(audioRecord, attributes);

            Method audioParamCheckMethod = AudioRecord.class.getDeclaredMethod("audioParamCheck", int.class, int.class, int.class);
            audioParamCheckMethod.setAccessible(true);
            audioParamCheckMethod.invoke(audioRecord, capturePreset, sampleRate, encoding);

            Field mChannelCountField = AudioRecord.class.getDeclaredField("mChannelCount");
            mChannelCountField.setAccessible(true);
            mChannelCountField.set(audioRecord, channels);

            Field mChannelMaskField = AudioRecord.class.getDeclaredField("mChannelMask");
            mChannelMaskField.setAccessible(true);
            mChannelMaskField.set(audioRecord, channelMask);

            int minBufferSize = AudioRecord.getMinBufferSize(sampleRate, channelConfig, encoding);
            int bufferSizeInBytes = minBufferSize * 8;

            Method audioBuffSizeCheckMethod = AudioRecord.class.getDeclaredMethod("audioBuffSizeCheck", int.class);
            audioBuffSizeCheckMethod.setAccessible(true);
            audioBuffSizeCheckMethod.invoke(audioRecord, bufferSizeInBytes);

            final int channelIndexMask = 0;

            int[] sampleRateArray = new int[]{sampleRate};
            int[] session = new int[]{AudioManager.AUDIO_SESSION_ID_GENERATE};

            int initResult;
            if (Build.VERSION.SDK_INT < AndroidVersions.API_31_ANDROID_12) {
                Method nativeSetupMethod = AudioRecord.class.getDeclaredMethod("native_setup", Object.class, Object.class, int[].class, int.class,
                        int.class, int.class, int.class, int[].class, String.class, long.class);
                nativeSetupMethod.setAccessible(true);
                initResult = (int) nativeSetupMethod.invoke(audioRecord, new WeakReference<AudioRecord>(audioRecord), attributes, sampleRateArray,
                        channelMask, channelIndexMask, audioRecord.getAudioFormat(), bufferSizeInBytes, session, FakeContext.get().getOpPackageName(),
                        0L);
            } else {
                AttributionSource attributionSource = FakeContext.get().getAttributionSource();

                Method asScopedParcelStateMethod = AttributionSource.class.getDeclaredMethod("asScopedParcelState");
                asScopedParcelStateMethod.setAccessible(true);

                try (AutoCloseable attributionSourceState = (AutoCloseable) asScopedParcelStateMethod.invoke(attributionSource)) {
                    Method getParcelMethod = attributionSourceState.getClass().getDeclaredMethod("getParcel");
                    Parcel attributionSourceParcel = (Parcel) getParcelMethod.invoke(attributionSourceState);

                    if (Build.VERSION.SDK_INT < AndroidVersions.API_34_ANDROID_14) {
                        Method nativeSetupMethod = AudioRecord.class.getDeclaredMethod("native_setup", Object.class, Object.class, int[].class,
                                int.class, int.class, int.class, int.class, int[].class, Parcel.class, long.class, int.class);
                        nativeSetupMethod.setAccessible(true);
                        initResult = (int) nativeSetupMethod.invoke(audioRecord, new WeakReference<AudioRecord>(audioRecord), attributes,
                                sampleRateArray, channelMask, channelIndexMask, audioRecord.getAudioFormat(), bufferSizeInBytes, session,
                                attributionSourceParcel, 0L, 0);
                    } else {
                        Method nativeSetupMethod = AudioRecord.class.getDeclaredMethod("native_setup", Object.class, Object.class, int[].class,
                                int.class, int.class, int.class, int.class, int[].class, Parcel.class, long.class, int.class, int.class);
                        nativeSetupMethod.setAccessible(true);
                        initResult = (int) nativeSetupMethod.invoke(audioRecord, new WeakReference<AudioRecord>(audioRecord), attributes,
                                sampleRateArray, channelMask, channelIndexMask, audioRecord.getAudioFormat(), bufferSizeInBytes, session,
                                attributionSourceParcel, 0L, 0, 0);
                    }
                }
            }

            if (initResult != AudioRecord.SUCCESS) {
                Ln.e("Error code " + initResult + " when initializing native AudioRecord object.");
                throw new RuntimeException("Cannot create AudioRecord");
            }

            Field mSampleRateField = AudioRecord.class.getDeclaredField("mSampleRate");
            mSampleRateField.setAccessible(true);
            mSampleRateField.set(audioRecord, sampleRateArray[0]);

            Field mSessionIdField = AudioRecord.class.getDeclaredField("mSessionId");
            mSessionIdField.setAccessible(true);
            mSessionIdField.set(audioRecord, session[0]);

            Field mStateField = AudioRecord.class.getDeclaredField("mState");
            mStateField.setAccessible(true);
            mStateField.set(audioRecord, AudioRecord.STATE_INITIALIZED);

            return audioRecord;
        } catch (Exception e) {
            Ln.e("Cannot create AudioRecord", e);
            throw new AudioCaptureException();
        }
    }
}
