// @flow
import React from "react";
import { Icon } from "ui";
import cn from "./TaskAccordion.less";

export type TaskAccordionProps = {
    customRender?: ?(target: { [key: string]: mixed }, path: string[]) => React.Element<*> | null,
    value: { [key: string]: mixed },
    title: string,
    pathPrefix: string[],
};

type TaskAccordionState = {
    collapsed: boolean,
};

export default class TaskAccordion extends React.Component {
    props: TaskAccordionProps;
    state: TaskAccordionState;
    static defaultProps = {
        pathPrefix: [],
    };

    componentWillMount() {
        this.state = {
            collapsed: false,
        };
    }

    render(): React.Element<*> {
        const { value, title } = this.props;
        const { collapsed } = this.state;

        return (
            <div className={cn("value-wrapper")}>
                {title &&
                    <button
                        data-tid="ToggleButton"
                        className={cn("toggle-button")}
                        onClick={() => this.setState({ collapsed: !collapsed })}>
                        <Icon name={collapsed ? "caret-right" : "caret-bottom"} />
                        <span data-tid="ToggleButtonText" className={cn("toggle-button-text")}>
                            {title}
                        </span>
                    </button>}
                {value && !collapsed && this.renderValue()}
            </div>
        );
    }

    renderValue(): React.Element<any>[] {
        const { value, customRender, pathPrefix } = this.props;
        const keys = Object.keys(value);

        return keys.map(key => {
            const valueToRender = value[key];
            if (typeof valueToRender === "object" && valueToRender != null && !Array.isArray(valueToRender)) {
                const newCustomRender = customRender ? (target, path) => customRender(value, [key, ...path]) : null;
                return (
                    <TaskAccordion
                        data-tid="InnerAccordion"
                        customRender={newCustomRender}
                        key={key}
                        value={valueToRender}
                        title={key}
                        pathPrefix={[...pathPrefix, key]}
                    />
                );
            }
            return (
                <div key={key} className={cn("string-wrapper")} data-tid={[...pathPrefix, key].join("_")}>
                    <span data-tid="Key" className={cn("title")}>
                        {key}:{" "}
                    </span>
                    <span data-tid="Value">
                        {(customRender && customRender(value, [key])) ||
                            (Array.isArray(value[key]) ? value[key].join(", ") : String(value[key]))}
                    </span>
                </div>
            );
        });
    }
}
