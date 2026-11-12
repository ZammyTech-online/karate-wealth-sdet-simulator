package com.soderberg.qa;

import com.intuit.karate.Results;
import com.intuit.karate.Runner;
import org.junit.jupiter.api.Test;
import static org.junit.jupiter.api.Assertions.*;

public class WealthRegressionRunner {

    @Test
    void testAllFeaturesParallel() {
        int threads = Integer.getInteger("karate.threads", 1);
        Results results = Runner.path("classpath:com/soderberg/qa/features")
                .outputCucumberJson(true)
                .parallel(threads);

        assertEquals(0, results.getFailCount(), results.getErrorMessages());
    }
}
