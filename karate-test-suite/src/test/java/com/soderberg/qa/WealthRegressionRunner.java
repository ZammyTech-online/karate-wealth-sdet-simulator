package com.soderberg.qa;

import com.intuit.karate.Results;
import com.intuit.karate.Runner;
import org.junit.jupiter.api.Test;
import static org.junit.jupiter.api.Assertions.*;

public class WealthRegressionRunner {

    @Test
    void testAllFeaturesParallel() {
        Results results = Runner.path("classpath:com/soderberg/qa/features")
                .outputCucumberJson(true)
                .parallel(3);

        assertEquals(0, results.getFailCount(), results.getErrorMessages());
    }
}
