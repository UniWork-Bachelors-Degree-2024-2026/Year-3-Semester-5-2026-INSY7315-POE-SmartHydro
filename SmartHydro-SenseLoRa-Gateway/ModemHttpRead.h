#pragma once
#include <string>
#include <stdlib.h>

// Read fragmented HTTPDATA prompts without waiting for Stream's idle timeout.
class SHHttpUploadPrompt {
    std::string text;
    bool ready = false, error = false;
public:
    void feed(char byte) {
        if (ready || error) return;
        if (text.size() >= 128) { error = true; return; }
        text += byte;
        error = text.find("ERROR") != std::string::npos;
        ready = !error && (text.find("DOWNLOAD") != std::string::npos || byte == '>');
    }
    bool done() const { return ready; }
    bool failed() const { return error; }
};

// SIM7670 acknowledges HTTPREAD before delivering its length-prefixed body.
class SHHttpReadParser {
    size_t expected, remaining = 0;
    std::string line, content;
    bool finished = false, error = false;
public:
    explicit SHHttpReadParser(size_t length) : expected(length) {
        error = !length || length > 4096;
        if (!error) content.reserve(length);
    }
    void feed(char byte) {
        if (finished || error) return;
        if (remaining) { content += byte; --remaining; return; }
        if (byte == '\r') return;
        if (byte != '\n') {
            if (line.size() >= 128) error = true;
            else line += byte;
            return;
        }
        if (line == "ERROR" || line.find("+CME ERROR:") == 0) error = true;
        else if (line == "OK") {
            // SIM7600 places OK after the body; SIM7670 places it before it.
            if (content.size() == expected) finished = true;
        } else if (line.find("+HTTPREAD:") == 0) {
            const char* number = line.c_str() + 10;
            while (*number == ' ') ++number;
            if (std::string(number).find("DATA,") == 0) number += 5;
            char* end = nullptr;
            unsigned long count = strtoul(number, &end, 10);
            if (end == number || *end || count > expected - content.size()) error = true;
            else if (!count) {
                if (content.size() == expected) finished = true;
                else error = true;
            } else remaining = count;
        }
        line.clear();
    }
    bool done() const { return finished; }
    bool failed() const { return error; }
    const std::string& body() const { return content; }
};
