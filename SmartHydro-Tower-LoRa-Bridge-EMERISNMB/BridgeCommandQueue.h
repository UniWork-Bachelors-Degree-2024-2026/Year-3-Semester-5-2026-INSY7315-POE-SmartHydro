#pragma once
#include <stddef.h>
#include <stdint.h>

// A fresh command reply takes the place of history, never the other way round.
template<class Queue, class Frame, class CommandId, class IsTerminal, class ReportSequence>
static bool shQueueBridgeFrame(Queue& queue, const Frame& frame, CommandId commandId, IsTerminal isTerminal, ReportSequence sequence) {
    auto cid = commandId(frame);
    if (cid) {
        for (auto item = queue.begin(); item != queue.end();) {
            if (commandId(*item) == cid) {
                // A retried start receipt must not replace a queued stop/completion receipt.
                if (isTerminal(*item) && !isTerminal(frame)) return true;
                if (isTerminal(*item) == isTerminal(frame) && sequence(*item) && sequence(frame)
                    && sequence(*item) > sequence(frame)) return true;
                item = queue.erase(item);
            }
            else ++item;
        }
    }
    if (queue.size() >= 8) {
        if (!cid) return false;
        auto victim = queue.end();
        for (auto item = queue.begin(); item != queue.end(); ++item) if (!commandId(*item)) victim = item;
        if (victim == queue.end()) return false;
        queue.erase(victim);
    }
    if (cid) {
        auto position = queue.begin();
        while (position != queue.end() && commandId(*position)) ++position;
        queue.insert(position, frame);
    } else queue.push_back(frame);
    return true;
}
