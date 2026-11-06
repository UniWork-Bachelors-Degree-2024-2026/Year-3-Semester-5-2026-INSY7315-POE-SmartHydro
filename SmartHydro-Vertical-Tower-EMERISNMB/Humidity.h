#pragma once
#include <stdint.h>

namespace Eloquent {
    namespace ML {
        namespace Port {
            class RandomForestHumidity {
                public:
                    int predict(float *x) {
                        uint8_t votes[3] = { 0 };
                        for (int i = 0; i < 100; i++) {
                            if (x[0] <= 64.965f) {
                                votes[1] += 1;
                            } else {
                                if (x[0] <= 75.045f) {
                                    votes[2] += 1;
                                } else {
                                    votes[0] += 1;
                                }
                            }
                        }
                        uint8_t classIdx = 0;
                        float maxVotes = votes[0];
                        for (uint8_t i = 1; i < 3; i++) {
                            if (votes[i] > maxVotes) {
                                classIdx = i;
                                maxVotes = votes[i];
                            }
                        }
                        return classIdx;
                    }
            };
        }
    }
}
