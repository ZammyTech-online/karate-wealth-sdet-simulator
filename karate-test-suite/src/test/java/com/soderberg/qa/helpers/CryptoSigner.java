package com.soderberg.qa.helpers;

import javax.crypto.Mac;
import javax.crypto.spec.SecretKeySpec;
import java.nio.charset.StandardCharsets;

/**
 * Enterprise Java Interop Class for Söderberg & Partners Karate Suite.
 * Computes HMAC-SHA256 signatures required by secure transaction endpoints.
 */
public class CryptoSigner {

    private static final String DEFAULT_SECRET = "SoderbergSharedSecret2026";

    public static String signPayload(String payload) {
        return signPayload(payload, DEFAULT_SECRET);
    }

    public static String signPayload(String payload, String secretKey) {
        try {
            Mac sha256Hmac = Mac.getInstance("HmacSHA256");
            SecretKeySpec secretKeySpec = new SecretKeySpec(secretKey.getBytes(StandardCharsets.UTF_8), "HmacSHA256");
            sha256Hmac.init(secretKeySpec);
            byte[] signedBytes = sha256Hmac.doFinal(payload.getBytes(StandardCharsets.UTF_8));
            
            StringBuilder hexString = new StringBuilder();
            for (byte b : signedBytes) {
                String hex = Integer.toHexString(0xff & b);
                if (hex.length() == 1) {
                    hexString.append('0');
                }
                hexString.append(hex);
            }
            return hexString.toString();
        } catch (Exception e) {
            throw new RuntimeException("Error calculating cryptographic signature for payload", e);
        }
    }
}
